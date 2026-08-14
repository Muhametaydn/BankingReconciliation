using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationAuditRetentionMonitor : IDisposable
{
    public const string MeterName = "BankingReconciliation.AuditRetention";

    private readonly object _lock = new();
    private readonly TimeProvider _timeProvider;
    private readonly Meter _meter;
    private readonly Counter<long> _runCounter;
    private readonly Histogram<double> _runDuration;
    private ReconciliationAuditRetentionSnapshot _snapshot;

    public ReconciliationAuditRetentionMonitor(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _snapshot = new ReconciliationAuditRetentionSnapshot
        {
            MonitoringStartedAt = _timeProvider.GetUtcNow()
        };
        _meter = new Meter(MeterName, "1.0.0");
        _runCounter = _meter.CreateCounter<long>(
            "banking_reconciliation.audit_retention.runs",
            unit: "{run}");
        _runDuration = _meter.CreateHistogram<double>(
            "banking_reconciliation.audit_retention.run.duration",
            unit: "s");
        _meter.CreateObservableGauge(
            "banking_reconciliation.audit_retention.events.hot",
            () => GetSnapshot().HotEventCount,
            unit: "{event}");
        _meter.CreateObservableGauge(
            "banking_reconciliation.audit_retention.events.archived",
            () => GetSnapshot().ArchivedEventCount,
            unit: "{event}");
        _meter.CreateObservableGauge(
            "banking_reconciliation.audit_retention.external_archive.pending",
            () => GetSnapshot().PendingExternalArchiveCount,
            unit: "{event}");
        _meter.CreateObservableGauge(
            "banking_reconciliation.audit_retention.last_success.age",
            ObserveLastSuccessAge,
            unit: "s");
    }

    public ReconciliationAuditRetentionSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return _snapshot;
        }
    }

    public void UpdateStorageStatistics(ReconciliationAuditStorageStatistics statistics)
    {
        lock (_lock)
        {
            _snapshot = _snapshot with
            {
                HotEventCount = statistics.HotEventCount,
                ArchivedEventCount = statistics.ArchivedEventCount,
                PendingExternalArchiveCount = statistics.PendingExternalArchiveCount,
                OldestPendingExternalArchiveAt = statistics.OldestPendingExternalArchiveAt
            };
        }
    }

    public void MarkStarted(DateTimeOffset startedAt)
    {
        lock (_lock)
        {
            _snapshot = _snapshot with { LastStartedAt = startedAt };
        }
    }

    public void MarkSucceeded(
        DateTimeOffset completedAt,
        int archivedCount,
        int purgedCount,
        int externalArchivedCount) =>
        MarkCompleted(
            completedAt,
            "success",
            archivedCount,
            purgedCount,
            externalArchivedCount);

    public void MarkDisabled(DateTimeOffset completedAt) =>
        MarkCompleted(completedAt, "disabled", 0, 0, 0);

    public void MarkFailed(DateTimeOffset failedAt)
    {
        DateTimeOffset? startedAt;
        lock (_lock)
        {
            startedAt = _snapshot.LastStartedAt;
            _snapshot = _snapshot with { LastFailedAt = failedAt };
        }

        RecordRun("failure", startedAt, failedAt);
    }

    public void Dispose() => _meter.Dispose();

    private void MarkCompleted(
        DateTimeOffset completedAt,
        string outcome,
        int archivedCount,
        int purgedCount,
        int externalArchivedCount)
    {
        DateTimeOffset? startedAt;
        lock (_lock)
        {
            startedAt = _snapshot.LastStartedAt;
            _snapshot = _snapshot with
            {
                LastSucceededAt = completedAt,
                LastArchivedCount = archivedCount,
                LastPurgedCount = purgedCount,
                LastExternalArchivedCount = externalArchivedCount
            };
        }

        RecordRun(outcome, startedAt, completedAt);
    }

    private void RecordRun(
        string outcome,
        DateTimeOffset? startedAt,
        DateTimeOffset completedAt)
    {
        var tags = new TagList { { "outcome", outcome } };
        _runCounter.Add(1, tags);
        if (startedAt is not null)
        {
            _runDuration.Record(
                Math.Max(0, (completedAt - startedAt.Value).TotalSeconds),
                tags);
        }
    }

    private IEnumerable<Measurement<double>> ObserveLastSuccessAge()
    {
        var lastSucceededAt = GetSnapshot().LastSucceededAt;
        if (lastSucceededAt is not null)
        {
            yield return new Measurement<double>(
                Math.Max(0, (_timeProvider.GetUtcNow() - lastSucceededAt.Value).TotalSeconds));
        }
    }
}

public sealed record ReconciliationAuditRetentionSnapshot
{
    public DateTimeOffset MonitoringStartedAt { get; init; }
    public DateTimeOffset? LastStartedAt { get; init; }
    public DateTimeOffset? LastSucceededAt { get; init; }
    public DateTimeOffset? LastFailedAt { get; init; }
    public int LastArchivedCount { get; init; }
    public int LastPurgedCount { get; init; }
    public int LastExternalArchivedCount { get; init; }
    public int HotEventCount { get; init; }
    public int ArchivedEventCount { get; init; }
    public int PendingExternalArchiveCount { get; init; }
    public DateTimeOffset? OldestPendingExternalArchiveAt { get; init; }
}
