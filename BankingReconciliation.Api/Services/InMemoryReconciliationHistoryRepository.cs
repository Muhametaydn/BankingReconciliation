using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public class InMemoryReconciliationHistoryRepository : IReconciliationHistoryRepository
{
    private readonly object _syncRoot = new();
    private readonly List<ReconciliationBatch> _batches = [];
    private readonly TimeProvider _timeProvider;

    public InMemoryReconciliationHistoryRepository(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public ReconciliationBatch Add(
        string branchFileName,
        string bankFileName,
        long processingDurationMilliseconds,
        ReconciliationSummary summary,
        ReconciliationInputType inputType = ReconciliationInputType.UploadedFiles,
        string? initiatedBy = null)
    {
        var batch = new ReconciliationBatch
        {
            Id = Guid.NewGuid(),
            CreatedAt = _timeProvider.GetUtcNow(),
            Status = ReconciliationBatchStatus.Completed,
            InputType = inputType,
            InitiatedBy = initiatedBy,
            BranchFileName = branchFileName,
            BankFileName = bankFileName,
            ProcessingDurationMilliseconds = processingDurationMilliseconds,
            Summary = summary
        };

        lock (_syncRoot)
        {
            _batches.Add(batch);
        }

        return batch;
    }

    public ReconciliationBatch AddFailed(
        string branchFileName,
        string bankFileName,
        long processingDurationMilliseconds,
        string errorCode,
        string errorMessage,
        ReconciliationInputType inputType = ReconciliationInputType.UploadedFiles,
        string? initiatedBy = null)
    {
        var batch = new ReconciliationBatch
        {
            Id = Guid.NewGuid(),
            CreatedAt = _timeProvider.GetUtcNow(),
            Status = ReconciliationBatchStatus.Failed,
            InputType = inputType,
            ApprovalStatus = ReconciliationApprovalStatus.NotApplicable,
            InitiatedBy = initiatedBy,
            BranchFileName = branchFileName,
            BankFileName = bankFileName,
            ProcessingDurationMilliseconds = processingDurationMilliseconds,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Summary = new ReconciliationSummary()
        };

        lock (_syncRoot)
        {
            _batches.Add(batch);
        }

        return batch;
    }

    public ReconciliationBatch AddQueued(
        string branchFileName,
        string bankFileName,
        ReconciliationInputType inputType = ReconciliationInputType.UploadedFiles,
        Guid? id = null,
        string? temporaryStorageKey = null,
        string? initiatedBy = null)
    {
        ValidateTemporaryStorageKey(inputType, temporaryStorageKey);
        var batch = new ReconciliationBatch
        {
            Id = id ?? Guid.NewGuid(),
            CreatedAt = _timeProvider.GetUtcNow(),
            Status = ReconciliationBatchStatus.Queued,
            InputType = inputType,
            ApprovalStatus = ReconciliationApprovalStatus.NotApplicable,
            InitiatedBy = initiatedBy,
            BranchFileName = branchFileName,
            BankFileName = bankFileName,
            TemporaryStorageKey = inputType == ReconciliationInputType.UploadedFiles
                ? temporaryStorageKey
                : null
        };

        lock (_syncRoot)
        {
            _batches.Add(batch);
        }

        return batch;
    }

    public void MarkProcessing(Guid id)
    {
        lock (_syncRoot)
        {
            var batch = GetRequiredBatch(id);
            batch.Status = ReconciliationBatchStatus.Processing;
            ResetApproval(batch, ReconciliationApprovalStatus.NotApplicable);
        }
    }

    public IReadOnlyCollection<Guid> GetClaimableJobIds(
        ReconciliationInputType inputType,
        DateTimeOffset now,
        int take,
        string? temporaryStorageKey = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ValidateTemporaryStorageKey(inputType, temporaryStorageKey);

        lock (_syncRoot)
        {
            return _batches
                .Where(batch => batch.InputType == inputType &&
                    HasStorageAffinity(batch, inputType, temporaryStorageKey) &&
                    IsClaimable(batch, now))
                .OrderBy(batch => batch.NextAttemptAt ?? batch.CreatedAt)
                .Take(take)
                .Select(batch => batch.Id)
                .ToList();
        }
    }

    public bool TryClaimJob(
        Guid id,
        ReconciliationInputType inputType,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        string? temporaryStorageKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        ValidateTemporaryStorageKey(inputType, temporaryStorageKey);

        lock (_syncRoot)
        {
            var batch = _batches.FirstOrDefault(item => item.Id == id);
            if (batch is null ||
                batch.InputType != inputType ||
                !HasStorageAffinity(batch, inputType, temporaryStorageKey) ||
                !IsClaimable(batch, now))
            {
                return false;
            }

            batch.Status = ReconciliationBatchStatus.Processing;
            ResetApproval(batch, ReconciliationApprovalStatus.NotApplicable);
            batch.AttemptCount++;
            batch.LastAttemptAt = now;
            batch.NextAttemptAt = null;
            batch.LeaseOwner = leaseOwner;
            batch.LeaseExpiresAt = now.Add(leaseDuration);
            return true;
        }
    }

    public IReadOnlyCollection<Guid> GetActiveUploadedFileJobIds(
        string temporaryStorageKey,
        IReadOnlyCollection<Guid> batchIds)
    {
        ValidateTemporaryStorageKey(
            ReconciliationInputType.UploadedFiles,
            temporaryStorageKey);
        ArgumentNullException.ThrowIfNull(batchIds);
        if (batchIds.Count == 0)
        {
            return [];
        }

        var candidateIds = batchIds.ToHashSet();
        lock (_syncRoot)
        {
            return _batches
                .Where(batch =>
                    candidateIds.Contains(batch.Id) &&
                    batch.InputType == ReconciliationInputType.UploadedFiles &&
                    string.Equals(
                        batch.TemporaryStorageKey,
                        temporaryStorageKey,
                        StringComparison.Ordinal) &&
                    batch.Status is ReconciliationBatchStatus.Queued or
                        ReconciliationBatchStatus.Processing)
                .Select(batch => batch.Id)
                .ToList();
        }
    }

    public bool RenewJobLease(
        Guid id,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        lock (_syncRoot)
        {
            var batch = _batches.FirstOrDefault(item => item.Id == id);
            if (!HasActiveLease(batch, leaseOwner, now))
            {
                return false;
            }

            batch!.LeaseExpiresAt = now.Add(leaseDuration);
            return true;
        }
    }

    public bool TryCompleteClaimedJob(
        Guid id,
        string leaseOwner,
        long processingDurationMilliseconds,
        ReconciliationSummary summary)
    {
        lock (_syncRoot)
        {
            var batch = _batches.FirstOrDefault(item => item.Id == id);
            if (!HasOwnedLease(batch, leaseOwner))
            {
                return false;
            }

            batch!.Status = ReconciliationBatchStatus.Completed;
            ResetApproval(batch, ReconciliationApprovalStatus.Pending);
            batch.ProcessingDurationMilliseconds = processingDurationMilliseconds;
            batch.Summary = summary;
            batch.ErrorCode = null;
            batch.ErrorMessage = null;
            ClearLease(batch);
            return true;
        }
    }

    public ReconciliationJobFailureDisposition HandleClaimedJobFailure(
        Guid id,
        string leaseOwner,
        long processingDurationMilliseconds,
        string errorCode,
        string errorMessage,
        bool retryable,
        int maxAttempts,
        DateTimeOffset nextAttemptAt)
    {
        lock (_syncRoot)
        {
            var batch = _batches.FirstOrDefault(item => item.Id == id);
            if (!HasOwnedLease(batch, leaseOwner))
            {
                return ReconciliationJobFailureDisposition.LeaseLost;
            }

            batch!.ProcessingDurationMilliseconds = processingDurationMilliseconds;
            batch.ErrorCode = errorCode;
            batch.ErrorMessage = errorMessage;
            batch.Summary = new ReconciliationSummary();
            ResetApproval(batch, ReconciliationApprovalStatus.NotApplicable);
            ClearLease(batch);

            if (retryable && batch.AttemptCount < maxAttempts)
            {
                batch.Status = ReconciliationBatchStatus.Queued;
                batch.NextAttemptAt = nextAttemptAt;
                return ReconciliationJobFailureDisposition.RetryScheduled;
            }

            batch.Status = ReconciliationBatchStatus.Failed;
            batch.NextAttemptAt = null;
            return ReconciliationJobFailureDisposition.Failed;
        }
    }

    private static bool IsClaimable(ReconciliationBatch batch, DateTimeOffset now)
    {
        return (batch.Status == ReconciliationBatchStatus.Queued &&
                (batch.NextAttemptAt is null || batch.NextAttemptAt <= now)) ||
            (batch.Status == ReconciliationBatchStatus.Processing &&
                (batch.LeaseExpiresAt is null || batch.LeaseExpiresAt <= now));
    }

    private static bool HasStorageAffinity(
        ReconciliationBatch batch,
        ReconciliationInputType inputType,
        string? temporaryStorageKey) =>
        inputType != ReconciliationInputType.UploadedFiles ||
        string.Equals(
            batch.TemporaryStorageKey,
            temporaryStorageKey,
            StringComparison.Ordinal);

    private static void ValidateTemporaryStorageKey(
        ReconciliationInputType inputType,
        string? temporaryStorageKey)
    {
        if (inputType == ReconciliationInputType.UploadedFiles &&
            !Guid.TryParseExact(temporaryStorageKey, "N", out _))
        {
            throw new ArgumentException(
                "Uploaded-file jobs require a valid temporary storage key.",
                nameof(temporaryStorageKey));
        }
    }

    private static bool HasActiveLease(
        ReconciliationBatch? batch,
        string leaseOwner,
        DateTimeOffset now)
    {
        return HasOwnedLease(batch, leaseOwner) && batch!.LeaseExpiresAt > now;
    }

    private static bool HasOwnedLease(ReconciliationBatch? batch, string leaseOwner)
    {
        return batch is not null &&
            batch.Status == ReconciliationBatchStatus.Processing &&
            string.Equals(batch.LeaseOwner, leaseOwner, StringComparison.Ordinal);
    }

    private static void ClearLease(ReconciliationBatch batch)
    {
        batch.LeaseOwner = null;
        batch.LeaseExpiresAt = null;
    }

    public ReconciliationBatch Complete(
        Guid id,
        long processingDurationMilliseconds,
        ReconciliationSummary summary)
    {
        lock (_syncRoot)
        {
            var batch = GetRequiredBatch(id);
            batch.Status = ReconciliationBatchStatus.Completed;
            ResetApproval(batch, ReconciliationApprovalStatus.Pending);
            batch.ProcessingDurationMilliseconds = processingDurationMilliseconds;
            batch.Summary = summary;
            batch.ErrorCode = null;
            batch.ErrorMessage = null;
            batch.NextAttemptAt = null;
            ClearLease(batch);
            return batch;
        }
    }

    public ReconciliationBatch Fail(
        Guid id,
        long processingDurationMilliseconds,
        string errorCode,
        string errorMessage)
    {
        lock (_syncRoot)
        {
            var batch = GetRequiredBatch(id);
            batch.Status = ReconciliationBatchStatus.Failed;
            ResetApproval(batch, ReconciliationApprovalStatus.NotApplicable);
            batch.ProcessingDurationMilliseconds = processingDurationMilliseconds;
            batch.Summary = new ReconciliationSummary();
            batch.ErrorCode = errorCode;
            batch.ErrorMessage = errorMessage;
            batch.NextAttemptAt = null;
            ClearLease(batch);
            return batch;
        }
    }

    public ReconciliationApprovalDecisionResult DecideApproval(
        Guid id,
        ReconciliationApprovalDecision decision,
        string decisionBy,
        string? comment)
    {
        lock (_syncRoot)
        {
            var batch = _batches.FirstOrDefault(item => item.Id == id);
            if (batch is null)
            {
                return new ReconciliationApprovalDecisionResult(
                    ReconciliationApprovalDecisionOutcome.NotFound);
            }

            if (batch.Status != ReconciliationBatchStatus.Completed)
            {
                return new ReconciliationApprovalDecisionResult(
                    ReconciliationApprovalDecisionOutcome.BatchNotCompleted,
                    batch);
            }

            if (batch.ApprovalStatus != ReconciliationApprovalStatus.Pending)
            {
                return new ReconciliationApprovalDecisionResult(
                    ReconciliationApprovalDecisionOutcome.AlreadyDecided,
                    batch);
            }

            batch.ApprovalStatus = decision == ReconciliationApprovalDecision.Approve
                ? ReconciliationApprovalStatus.Approved
                : ReconciliationApprovalStatus.Rejected;
            batch.DecisionBy = decisionBy;
            batch.DecisionAt = _timeProvider.GetUtcNow();
            batch.DecisionComment = comment;

            return new ReconciliationApprovalDecisionResult(
                ReconciliationApprovalDecisionOutcome.Updated,
                batch);
        }
    }

    private static void ResetApproval(
        ReconciliationBatch batch,
        ReconciliationApprovalStatus approvalStatus)
    {
        batch.ApprovalStatus = approvalStatus;
        batch.DecisionBy = null;
        batch.DecisionAt = null;
        batch.DecisionComment = null;
    }

    private ReconciliationBatch GetRequiredBatch(Guid id)
    {
        return _batches.FirstOrDefault(batch => batch.Id == id) ??
            throw new InvalidOperationException($"Reconciliation batch '{id}' was not found.");
    }

    public IReadOnlyCollection<ReconciliationBatch> GetAll(ReconciliationHistoryQuery? query = null)
    {
        query ??= new ReconciliationHistoryQuery();

        lock (_syncRoot)
        {
            return _batches
                .Where(batch => MatchesQuery(batch, query))
                .OrderByDescending(batch => batch.CreatedAt)
                .Skip(query.Skip)
                .Take(query.Take)
                .ToList();
        }
    }

    public int Count(ReconciliationHistoryQuery? query = null)
    {
        query ??= new ReconciliationHistoryQuery();

        lock (_syncRoot)
        {
            return _batches.Count(batch => MatchesQuery(batch, query));
        }
    }

    private static bool MatchesQuery(ReconciliationBatch batch, ReconciliationHistoryQuery query)
    {
        return (query.From is null || batch.CreatedAt >= query.From) &&
            (query.To is null || batch.CreatedAt <= query.To) &&
            (query.Status is null || batch.Status == query.Status) &&
            (query.InputType is null || batch.InputType == query.InputType) &&
            MatchesSearch(batch, query.Search);
    }

    private static bool MatchesSearch(ReconciliationBatch batch, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var value = search.Trim();
        return batch.BranchFileName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            batch.BankFileName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            (batch.ErrorCode?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (batch.ErrorMessage?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public ReconciliationBatch? GetById(Guid id)
    {
        lock (_syncRoot)
        {
            return _batches.FirstOrDefault(batch => batch.Id == id);
        }
    }
}
