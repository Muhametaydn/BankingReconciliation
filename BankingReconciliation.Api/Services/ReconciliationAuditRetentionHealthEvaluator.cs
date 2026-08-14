using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationAuditRetentionHealthEvaluator
{
    private readonly IReconciliationAuditRepository _repository;
    private readonly IReconciliationImmutableAuditArchive _immutableArchive;
    private readonly ReconciliationAuditRetentionMonitor _monitor;
    private readonly ReconciliationAuditRetentionOptions _options;
    private readonly TimeProvider _timeProvider;

    public ReconciliationAuditRetentionHealthEvaluator(
        IReconciliationAuditRepository repository,
        IReconciliationImmutableAuditArchive immutableArchive,
        ReconciliationAuditRetentionMonitor monitor,
        IOptions<ReconciliationAuditRetentionOptions> options,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _immutableArchive = immutableArchive;
        _monitor = monitor;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public ReconciliationAuditRetentionHealthSnapshot Evaluate()
    {
        var storage = _repository.GetStorageStatistics();
        _monitor.UpdateStorageStatistics(storage);
        var execution = _monitor.GetSnapshot();
        var now = _timeProvider.GetUtcNow();
        var alerts = new List<string>();

        var lastRunFailed = execution.LastFailedAt is not null &&
            (execution.LastSucceededAt is null || execution.LastFailedAt > execution.LastSucceededAt);
        if (lastRunFailed)
        {
            alerts.Add("LastRunFailed");
        }

        var latestCompletion = execution.LastSucceededAt;
        var referenceTime = latestCompletion ?? execution.MonitoringStartedAt;
        if (_options.Enabled && now - referenceTime > TimeSpan.FromHours(_options.MaximumRunLatenessHours))
        {
            alerts.Add("RunOverdue");
        }

        var pendingExternalCount = _immutableArchive.Enabled
            ? storage.PendingExternalArchiveCount
            : 0;
        var oldestPendingAt = _immutableArchive.Enabled
            ? storage.OldestPendingExternalArchiveAt
            : null;
        if (_immutableArchive.Enabled &&
            pendingExternalCount >= _options.ExternalArchiveBacklogAlertCount)
        {
            alerts.Add("ExternalArchiveBacklogCount");
        }
        if (_immutableArchive.Enabled && oldestPendingAt is not null &&
            now - oldestPendingAt.Value >
                TimeSpan.FromHours(_options.ExternalArchiveBacklogAlertAgeHours))
        {
            alerts.Add("ExternalArchiveBacklogAge");
        }

        var status = !_options.Enabled
            ? "Disabled"
            : alerts.Count > 0
                ? "Degraded"
                : pendingExternalCount > 0
                    ? "Backlog"
                    : "Ready";

        return new ReconciliationAuditRetentionHealthSnapshot(
            status,
            alerts,
            storage,
            execution,
            pendingExternalCount,
            oldestPendingAt,
            _immutableArchive.Enabled);
    }
}

public sealed record ReconciliationAuditRetentionHealthSnapshot(
    string Status,
    IReadOnlyList<string> Alerts,
    ReconciliationAuditStorageStatistics Storage,
    ReconciliationAuditRetentionSnapshot Execution,
    int PendingExternalArchiveCount,
    DateTimeOffset? OldestPendingExternalArchiveAt,
    bool ImmutableArchiveEnabled);
