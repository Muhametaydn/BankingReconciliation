using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public interface IReconciliationAuditRepository
{
    ReconciliationAuditEvent Add(
        ReconciliationAuditAction action,
        string actor,
        ReconciliationAuditResourceType resourceType,
        string resourceId,
        object? beforeState,
        object? afterState);

    IReadOnlyCollection<ReconciliationAuditEvent> GetAll(ReconciliationAuditQuery? query = null);
    int Count(ReconciliationAuditQuery? query = null);
    Task<ReconciliationAuditRetentionResult> ArchiveAndPurgeAsync(
        DateTimeOffset hotCutoff,
        DateTimeOffset? archiveCutoff,
        int batchSize,
        bool requireExternalArchive = false,
        CancellationToken cancellationToken = default);
    IReadOnlyCollection<ReconciliationAuditEvent> GetPendingExternalArchive(int take);
    void MarkExternalArchived(
        IReadOnlyCollection<Guid> eventIds,
        string objectKey,
        DateTimeOffset archivedAt);
    ReconciliationAuditStorageStatistics GetStorageStatistics();
}

public sealed record ReconciliationAuditRetentionResult(int ArchivedCount, int PurgedCount);

public sealed record ReconciliationAuditStorageStatistics(
    int HotEventCount,
    int ArchivedEventCount,
    int PendingExternalArchiveCount,
    DateTimeOffset? OldestPendingExternalArchiveAt);
