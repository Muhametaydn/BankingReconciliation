using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public interface IReconciliationHistoryRepository
{
    ReconciliationBatch Add(
        string branchFileName,
        string bankFileName,
        long processingDurationMilliseconds,
        ReconciliationSummary summary,
        ReconciliationInputType inputType = ReconciliationInputType.UploadedFiles,
        string? initiatedBy = null);

    ReconciliationBatch AddFailed(
        string branchFileName,
        string bankFileName,
        long processingDurationMilliseconds,
        string errorCode,
        string errorMessage,
        ReconciliationInputType inputType = ReconciliationInputType.UploadedFiles,
        string? initiatedBy = null);

    ReconciliationBatch AddQueued(
        string branchFileName,
        string bankFileName,
        ReconciliationInputType inputType = ReconciliationInputType.UploadedFiles,
        Guid? id = null,
        string? temporaryStorageKey = null,
        string? initiatedBy = null);

    void MarkProcessing(Guid id);

    IReadOnlyCollection<Guid> GetClaimableJobIds(
        ReconciliationInputType inputType,
        DateTimeOffset now,
        int take,
        string? temporaryStorageKey = null);

    bool TryClaimJob(
        Guid id,
        ReconciliationInputType inputType,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        string? temporaryStorageKey = null);

    IReadOnlyCollection<Guid> GetActiveUploadedFileJobIds(
        string temporaryStorageKey,
        IReadOnlyCollection<Guid> batchIds);

    bool RenewJobLease(
        Guid id,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration);

    bool TryCompleteClaimedJob(
        Guid id,
        string leaseOwner,
        long processingDurationMilliseconds,
        ReconciliationSummary summary);

    ReconciliationJobFailureDisposition HandleClaimedJobFailure(
        Guid id,
        string leaseOwner,
        long processingDurationMilliseconds,
        string errorCode,
        string errorMessage,
        bool retryable,
        int maxAttempts,
        DateTimeOffset nextAttemptAt);

    ReconciliationBatch Complete(Guid id, long processingDurationMilliseconds, ReconciliationSummary summary);

    ReconciliationBatch Fail(
        Guid id,
        long processingDurationMilliseconds,
        string errorCode,
        string errorMessage);

    ReconciliationApprovalDecisionResult DecideApproval(
        Guid id,
        ReconciliationApprovalDecision decision,
        string decisionBy,
        string? comment);

    IReadOnlyCollection<ReconciliationBatch> GetAll(ReconciliationHistoryQuery? query = null);

    int Count(ReconciliationHistoryQuery? query = null);

    ReconciliationBatch? GetById(Guid id);
}
