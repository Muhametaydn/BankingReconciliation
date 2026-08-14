using System.Diagnostics.Metrics;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class ReconciliationAuditRetentionTests
{
    [Fact]
    public async Task RunOnceAsync_ArchivesHotEvents_PurgesExpiredArchives_AndKeepsHistorySearchable()
    {
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(now.AddDays(-3000));
        var repository = new InMemoryReconciliationAuditRepository(timeProvider);
        var ancient = AddEvent(repository, "ancient-admin");
        timeProvider.UtcNow = now.AddDays(-400);
        var archived = AddEvent(repository, "archived-admin");
        timeProvider.UtcNow = now;
        var recent = AddEvent(repository, "recent-admin");

        using var services = new ServiceCollection()
            .AddSingleton<IReconciliationAuditRepository>(repository)
            .BuildServiceProvider();
        var monitor = new ReconciliationAuditRetentionMonitor();
        using var service = new ReconciliationAuditRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(),
            timeProvider,
            Options.Create(new ReconciliationAuditRetentionOptions
            {
                HotRetentionDays = 365,
                ArchiveRetentionDays = 2555,
                BatchSize = 100
            }),
            new DisabledReconciliationImmutableAuditArchive(),
            monitor,
            NullLogger<ReconciliationAuditRetentionService>.Instance);

        var result = await service.RunOnceAsync();

        Assert.Equal(2, result.ArchivedCount);
        Assert.Equal(1, result.PurgedCount);
        Assert.Equal(2, repository.Count());
        Assert.DoesNotContain(repository.GetAll(), item => item.Id == ancient.Id);
        var archivedResult = Assert.Single(
            repository.GetAll().Where(item => item.Id == archived.Id));
        Assert.Equal(now, archivedResult.ArchivedAt);
        Assert.Matches("^[a-f0-9]{64}$", archivedResult.IntegrityHash);
        Assert.True(archivedResult.IntegrityVerified);
        Assert.Contains(repository.GetAll(), item => item.Id == recent.Id && item.ArchivedAt is null);
        var statistics = repository.GetStorageStatistics();
        Assert.Equal(1, statistics.HotEventCount);
        Assert.Equal(1, statistics.ArchivedEventCount);
        Assert.Equal(1, statistics.PendingExternalArchiveCount);
        var execution = monitor.GetSnapshot();
        Assert.Equal(now, execution.LastSucceededAt);
        Assert.Equal(2, execution.LastArchivedCount);
        Assert.Equal(1, execution.LastPurgedCount);
    }

    [Fact]
    public void IntegrityHash_IsDeterministic_AndDetectsContentChanges()
    {
        var auditEvent = new ReconciliationAuditEvent
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CreatedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero),
            Actor = "admin",
            Action = ReconciliationAuditAction.SourceUpdated,
            ResourceType = ReconciliationAuditResourceType.ReconciliationSource,
            ResourceId = "source-1",
            BeforeStateJson = "{\"name\":\"before\"}",
            AfterStateJson = "{\"name\":\"after\"}"
        };

        var original = ReconciliationAuditIntegrity.ComputeHash(auditEvent);
        Assert.Equal(original, ReconciliationAuditIntegrity.ComputeHash(auditEvent));

        auditEvent.AfterStateJson = "{\"name\":\"changed\"}";
        Assert.NotEqual(original, ReconciliationAuditIntegrity.ComputeHash(auditEvent));
    }

    [Fact]
    public void OptionsValidator_AcceptsIndefiniteArchive_AndRejectsShortArchive()
    {
        Assert.True(ReconciliationAuditRetentionOptionsValidator.IsValid(
            new ReconciliationAuditRetentionOptions
            {
                HotRetentionDays = 365,
                ArchiveRetentionDays = null
            }));
        Assert.False(ReconciliationAuditRetentionOptionsValidator.IsValid(
            new ReconciliationAuditRetentionOptions
            {
                HotRetentionDays = 365,
                ArchiveRetentionDays = 365
            }));
        Assert.False(ReconciliationAuditRetentionOptionsValidator.IsValid(
            new ReconciliationAuditRetentionOptions
            {
                CleanupIntervalHours = 24,
                MaximumRunLatenessHours = 12
            }));
    }

    [Fact]
    public void Monitor_EmitsBoundedOpenTelemetryMetrics()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(now);
        var measurements = new List<(string Name, double Value, string? Outcome)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ReconciliationAuditRetentionMonitor.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                {
                    outcome = tag.Value?.ToString();
                }
            }
            measurements.Add((instrument.Name, value, outcome));
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                {
                    outcome = tag.Value?.ToString();
                }
            }
            measurements.Add((instrument.Name, value, outcome));
        });
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
        {
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                {
                    outcome = tag.Value?.ToString();
                }
            }
            measurements.Add((instrument.Name, value, outcome));
        });
        listener.Start();

        using var monitor = new ReconciliationAuditRetentionMonitor(timeProvider);
        monitor.UpdateStorageStatistics(new ReconciliationAuditStorageStatistics(7, 11, 3, now));
        monitor.MarkStarted(now);
        timeProvider.UtcNow = now.AddSeconds(2);
        monitor.MarkSucceeded(timeProvider.UtcNow, 4, 2, 3);
        listener.RecordObservableInstruments();

        Assert.Contains(measurements, item =>
            item.Name == "banking_reconciliation.audit_retention.runs" &&
            item.Value == 1 && item.Outcome == "success");
        Assert.Contains(measurements, item =>
            item.Name == "banking_reconciliation.audit_retention.run.duration" &&
            item.Value == 2);
        Assert.Contains(measurements, item =>
            item.Name == "banking_reconciliation.audit_retention.events.hot" &&
            item.Value == 7);
        Assert.Contains(measurements, item =>
            item.Name == "banking_reconciliation.audit_retention.external_archive.pending" &&
            item.Value == 3);
    }

    [Fact]
    public void ObservabilityValidator_RequiresAbsoluteHttpEndpoint_WhenEnabled()
    {
        Assert.True(ReconciliationObservabilityOptionsValidator.IsValid(
            new ReconciliationObservabilityOptions()));
        Assert.True(ReconciliationObservabilityOptionsValidator.IsValid(
            new ReconciliationObservabilityOptions
            {
                OpenTelemetryEnabled = true,
                OtlpEndpoint = "http://otel-collector:4317"
            }));
        Assert.False(ReconciliationObservabilityOptionsValidator.IsValid(
            new ReconciliationObservabilityOptions
            {
                OpenTelemetryEnabled = true,
                OtlpEndpoint = "collector-without-scheme"
            }));
    }

    [Fact]
    public async Task HealthEvaluator_AlertsWhenExternalArchiveBacklogExceedsThresholds()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(now.AddDays(-2));
        var repository = new InMemoryReconciliationAuditRepository(timeProvider);
        AddEvent(repository, "backlog-admin");
        timeProvider.UtcNow = now;
        await repository.ArchiveAndPurgeAsync(
            now.AddDays(-1),
            archiveCutoff: null,
            batchSize: 100,
            requireExternalArchive: true);
        using var monitor = new ReconciliationAuditRetentionMonitor(timeProvider);
        var evaluator = new ReconciliationAuditRetentionHealthEvaluator(
            repository,
            new RecordingImmutableArchive(),
            monitor,
            Options.Create(new ReconciliationAuditRetentionOptions
            {
                ExternalArchiveBacklogAlertCount = 1,
                ExternalArchiveBacklogAlertAgeHours = 24
            }),
            timeProvider);

        var health = evaluator.Evaluate();

        Assert.Equal("Degraded", health.Status);
        Assert.Contains("ExternalArchiveBacklogCount", health.Alerts);
        Assert.Contains("ExternalArchiveBacklogAge", health.Alerts);
        Assert.Equal(1, health.PendingExternalArchiveCount);
    }

    [Fact]
    public async Task RunOnceAsync_RequiresExternalArchiveBeforePurge_WhenEnabled()
    {
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(now.AddDays(-3000));
        var repository = new InMemoryReconciliationAuditRepository(timeProvider);
        var auditEvent = AddEvent(repository, "worm-admin");
        timeProvider.UtcNow = now;
        var immutableArchive = new RecordingImmutableArchive();
        using var services = new ServiceCollection()
            .AddSingleton<IReconciliationAuditRepository>(repository)
            .BuildServiceProvider();
        var monitor = new ReconciliationAuditRetentionMonitor();
        using var service = new ReconciliationAuditRetentionService(
            services.GetRequiredService<IServiceScopeFactory>(),
            timeProvider,
            Options.Create(new ReconciliationAuditRetentionOptions
            {
                HotRetentionDays = 365,
                ArchiveRetentionDays = 2555,
                BatchSize = 100
            }),
            immutableArchive,
            monitor,
            NullLogger<ReconciliationAuditRetentionService>.Instance);

        var first = await service.RunOnceAsync();
        var externallyArchived = Assert.Single(repository.GetAll());
        Assert.Equal(1, first.ArchivedCount);
        Assert.Equal(0, first.PurgedCount);
        Assert.Equal(auditEvent.Id, externallyArchived.Id);
        Assert.Equal("immutable/test-object.json", externallyArchived.ExternalArchiveKey);
        Assert.Equal(now, externallyArchived.ExternalArchivedAt);
        Assert.Equal(0, repository.GetStorageStatistics().PendingExternalArchiveCount);
        Assert.Equal(1, monitor.GetSnapshot().LastExternalArchivedCount);

        var second = await service.RunOnceAsync();
        Assert.Equal(1, second.PurgedCount);
        Assert.Empty(repository.GetAll());
    }

    private static ReconciliationAuditEvent AddEvent(
        InMemoryReconciliationAuditRepository repository,
        string actor) => repository.Add(
            ReconciliationAuditAction.SourceUpdated,
            actor,
            ReconciliationAuditResourceType.ReconciliationSource,
            Guid.NewGuid().ToString("N"),
            beforeState: null,
            afterState: new { Enabled = true });

    private sealed class MutableTimeProvider : TimeProvider
    {
        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingImmutableArchive : IReconciliationImmutableAuditArchive
    {
        public bool Enabled => true;

        public Task<string> WriteAsync(
            IReadOnlyCollection<ReconciliationAuditEvent> events,
            CancellationToken cancellationToken = default)
        {
            Assert.NotEmpty(events);
            return Task.FromResult("immutable/test-object.json");
        }
    }
}
