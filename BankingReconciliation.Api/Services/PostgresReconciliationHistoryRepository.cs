using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BankingReconciliation.Api.Services;

public class PostgresReconciliationHistoryRepository : IReconciliationHistoryRepository
{
    private readonly ReconciliationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public PostgresReconciliationHistoryRepository(
        ReconciliationDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
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

        _dbContext.ReconciliationBatches.Add(ToEntity(batch));
        _dbContext.SaveChanges();

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

        _dbContext.ReconciliationBatches.Add(ToEntity(batch));
        _dbContext.SaveChanges();

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

        _dbContext.ReconciliationBatches.Add(ToEntity(batch));
        _dbContext.SaveChanges();
        return batch;
    }

    public void MarkProcessing(Guid id)
    {
        var entity = GetRequiredEntity(id);
        entity.Status = ReconciliationBatchStatus.Processing;
        ResetApproval(entity, ReconciliationApprovalStatus.NotApplicable);
        _dbContext.SaveChanges();
    }

    public IReadOnlyCollection<Guid> GetClaimableJobIds(
        ReconciliationInputType inputType,
        DateTimeOffset now,
        int take,
        string? temporaryStorageKey = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ValidateTemporaryStorageKey(inputType, temporaryStorageKey);

        return _dbContext.ReconciliationBatches
            .AsNoTracking()
            .Where(batch => batch.InputType == inputType &&
                (inputType != ReconciliationInputType.UploadedFiles ||
                    batch.TemporaryStorageKey == temporaryStorageKey) &&
                ((batch.Status == ReconciliationBatchStatus.Queued &&
                    (batch.NextAttemptAt == null || batch.NextAttemptAt <= now)) ||
                 (batch.Status == ReconciliationBatchStatus.Processing &&
                    (batch.LeaseExpiresAt == null || batch.LeaseExpiresAt <= now))))
            .OrderBy(batch => batch.NextAttemptAt ?? batch.CreatedAt)
            .Take(take)
            .Select(batch => batch.Id)
            .ToList();
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

        var leaseExpiresAt = now.Add(leaseDuration);
        var updatedCount = _dbContext.ReconciliationBatches
            .Where(batch => batch.Id == id &&
                batch.InputType == inputType &&
                (inputType != ReconciliationInputType.UploadedFiles ||
                    batch.TemporaryStorageKey == temporaryStorageKey) &&
                ((batch.Status == ReconciliationBatchStatus.Queued &&
                    (batch.NextAttemptAt == null || batch.NextAttemptAt <= now)) ||
                 (batch.Status == ReconciliationBatchStatus.Processing &&
                    (batch.LeaseExpiresAt == null || batch.LeaseExpiresAt <= now))))
            .ExecuteUpdate(setters => setters
                .SetProperty(batch => batch.Status, ReconciliationBatchStatus.Processing)
                .SetProperty(batch => batch.ApprovalStatus, ReconciliationApprovalStatus.NotApplicable)
                .SetProperty(batch => batch.DecisionBy, (string?)null)
                .SetProperty(batch => batch.DecisionAt, (DateTimeOffset?)null)
                .SetProperty(batch => batch.DecisionComment, (string?)null)
                .SetProperty(batch => batch.AttemptCount, batch => batch.AttemptCount + 1)
                .SetProperty(batch => batch.LastAttemptAt, now)
                .SetProperty(batch => batch.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(batch => batch.LeaseOwner, leaseOwner)
                .SetProperty(batch => batch.LeaseExpiresAt, leaseExpiresAt));

        return updatedCount == 1;
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

        return _dbContext.ReconciliationBatches
            .AsNoTracking()
            .Where(batch =>
                batchIds.Contains(batch.Id) &&
                batch.InputType == ReconciliationInputType.UploadedFiles &&
                batch.TemporaryStorageKey == temporaryStorageKey &&
                (batch.Status == ReconciliationBatchStatus.Queued ||
                    batch.Status == ReconciliationBatchStatus.Processing))
            .Select(batch => batch.Id)
            .ToList();
    }

    public bool RenewJobLease(
        Guid id,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        var leaseExpiresAt = now.Add(leaseDuration);
        var updatedCount = _dbContext.ReconciliationBatches
            .Where(batch => batch.Id == id &&
                batch.Status == ReconciliationBatchStatus.Processing &&
                batch.LeaseOwner == leaseOwner &&
                batch.LeaseExpiresAt > now)
            .ExecuteUpdate(setters => setters
                .SetProperty(batch => batch.LeaseExpiresAt, leaseExpiresAt));

        return updatedCount == 1;
    }

    public bool TryCompleteClaimedJob(
        Guid id,
        string leaseOwner,
        long processingDurationMilliseconds,
        ReconciliationSummary summary)
    {
        using var transaction = _dbContext.Database.BeginTransaction();
        var updatedCount = _dbContext.ReconciliationBatches
            .Where(batch => batch.Id == id &&
                batch.Status == ReconciliationBatchStatus.Processing &&
                batch.LeaseOwner == leaseOwner)
            .ExecuteUpdate(setters => setters
                .SetProperty(batch => batch.Status, ReconciliationBatchStatus.Completed)
                .SetProperty(batch => batch.ApprovalStatus, ReconciliationApprovalStatus.Pending)
                .SetProperty(batch => batch.DecisionBy, (string?)null)
                .SetProperty(batch => batch.DecisionAt, (DateTimeOffset?)null)
                .SetProperty(batch => batch.DecisionComment, (string?)null)
                .SetProperty(batch => batch.ProcessingDurationMilliseconds, processingDurationMilliseconds)
                .SetProperty(batch => batch.ErrorCode, (string?)null)
                .SetProperty(batch => batch.ErrorMessage, (string?)null)
                .SetProperty(batch => batch.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(batch => batch.LeaseOwner, (string?)null)
                .SetProperty(batch => batch.LeaseExpiresAt, (DateTimeOffset?)null));

        if (updatedCount != 1)
        {
            transaction.Rollback();
            return false;
        }

        var entity = _dbContext.ReconciliationBatches
            .Include(batch => batch.Differences)
            .Single(batch => batch.Id == id);
        ApplySummary(entity, summary);
        _dbContext.SaveChanges();
        transaction.Commit();
        return true;
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
        using var transaction = _dbContext.Database.BeginTransaction();
        if (retryable)
        {
            var retryUpdatedCount = _dbContext.ReconciliationBatches
                .Where(batch => batch.Id == id &&
                    batch.Status == ReconciliationBatchStatus.Processing &&
                    batch.LeaseOwner == leaseOwner &&
                    batch.AttemptCount < maxAttempts)
                .ExecuteUpdate(setters => setters
                    .SetProperty(batch => batch.Status, ReconciliationBatchStatus.Queued)
                    .SetProperty(batch => batch.ApprovalStatus, ReconciliationApprovalStatus.NotApplicable)
                    .SetProperty(batch => batch.DecisionBy, (string?)null)
                    .SetProperty(batch => batch.DecisionAt, (DateTimeOffset?)null)
                    .SetProperty(batch => batch.DecisionComment, (string?)null)
                    .SetProperty(batch => batch.ProcessingDurationMilliseconds, processingDurationMilliseconds)
                    .SetProperty(batch => batch.ErrorCode, errorCode)
                    .SetProperty(batch => batch.ErrorMessage, errorMessage)
                    .SetProperty(batch => batch.TotalBranchRecords, 0)
                    .SetProperty(batch => batch.TotalBankRecords, 0)
                    .SetProperty(batch => batch.MatchedCount, 0)
                    .SetProperty(batch => batch.OnlyInBranchCount, 0)
                    .SetProperty(batch => batch.OnlyInBankCount, 0)
                    .SetProperty(batch => batch.MismatchCount, 0)
                    .SetProperty(batch => batch.NextAttemptAt, nextAttemptAt)
                    .SetProperty(batch => batch.LeaseOwner, (string?)null)
                    .SetProperty(batch => batch.LeaseExpiresAt, (DateTimeOffset?)null));

            if (retryUpdatedCount == 1)
            {
                DeleteDifferences(id);
                transaction.Commit();
                return ReconciliationJobFailureDisposition.RetryScheduled;
            }
        }

        var failedUpdatedCount = _dbContext.ReconciliationBatches
            .Where(batch => batch.Id == id &&
                batch.Status == ReconciliationBatchStatus.Processing &&
                batch.LeaseOwner == leaseOwner)
            .ExecuteUpdate(setters => setters
                .SetProperty(batch => batch.Status, ReconciliationBatchStatus.Failed)
                .SetProperty(batch => batch.ApprovalStatus, ReconciliationApprovalStatus.NotApplicable)
                .SetProperty(batch => batch.DecisionBy, (string?)null)
                .SetProperty(batch => batch.DecisionAt, (DateTimeOffset?)null)
                .SetProperty(batch => batch.DecisionComment, (string?)null)
                .SetProperty(batch => batch.ProcessingDurationMilliseconds, processingDurationMilliseconds)
                .SetProperty(batch => batch.ErrorCode, errorCode)
                .SetProperty(batch => batch.ErrorMessage, errorMessage)
                .SetProperty(batch => batch.TotalBranchRecords, 0)
                .SetProperty(batch => batch.TotalBankRecords, 0)
                .SetProperty(batch => batch.MatchedCount, 0)
                .SetProperty(batch => batch.OnlyInBranchCount, 0)
                .SetProperty(batch => batch.OnlyInBankCount, 0)
                .SetProperty(batch => batch.MismatchCount, 0)
                .SetProperty(batch => batch.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(batch => batch.LeaseOwner, (string?)null)
                .SetProperty(batch => batch.LeaseExpiresAt, (DateTimeOffset?)null));

        if (failedUpdatedCount != 1)
        {
            transaction.Rollback();
            return ReconciliationJobFailureDisposition.LeaseLost;
        }

        DeleteDifferences(id);
        transaction.Commit();
        return ReconciliationJobFailureDisposition.Failed;
    }

    private void DeleteDifferences(Guid id)
    {
        _dbContext.ReconciliationDifferences
            .Where(difference => difference.BatchId == id)
            .ExecuteDelete();
    }

    public ReconciliationBatch Complete(
        Guid id,
        long processingDurationMilliseconds,
        ReconciliationSummary summary)
    {
        var entity = _dbContext.ReconciliationBatches
            .Include(batch => batch.Differences)
            .SingleOrDefault(batch => batch.Id == id) ??
            throw new InvalidOperationException($"Reconciliation batch '{id}' was not found.");
        entity.Status = ReconciliationBatchStatus.Completed;
        ResetApproval(entity, ReconciliationApprovalStatus.Pending);
        entity.ProcessingDurationMilliseconds = processingDurationMilliseconds;
        entity.ErrorCode = null;
        entity.ErrorMessage = null;
        entity.NextAttemptAt = null;
        entity.LeaseOwner = null;
        entity.LeaseExpiresAt = null;
        ApplySummary(entity, summary);
        _dbContext.SaveChanges();
        return ToDetailBatch(entity);
    }

    public ReconciliationBatch Fail(
        Guid id,
        long processingDurationMilliseconds,
        string errorCode,
        string errorMessage)
    {
        var entity = _dbContext.ReconciliationBatches
            .Include(batch => batch.Differences)
            .SingleOrDefault(batch => batch.Id == id) ??
            throw new InvalidOperationException($"Reconciliation batch '{id}' was not found.");
        entity.Status = ReconciliationBatchStatus.Failed;
        ResetApproval(entity, ReconciliationApprovalStatus.NotApplicable);
        entity.ProcessingDurationMilliseconds = processingDurationMilliseconds;
        entity.ErrorCode = errorCode;
        entity.ErrorMessage = errorMessage;
        entity.NextAttemptAt = null;
        entity.LeaseOwner = null;
        entity.LeaseExpiresAt = null;
        ApplySummary(entity, new ReconciliationSummary());
        _dbContext.SaveChanges();
        return ToDetailBatch(entity);
    }

    public ReconciliationApprovalDecisionResult DecideApproval(
        Guid id,
        ReconciliationApprovalDecision decision,
        string decisionBy,
        string? comment)
    {
        var approvalStatus = decision == ReconciliationApprovalDecision.Approve
            ? ReconciliationApprovalStatus.Approved
            : ReconciliationApprovalStatus.Rejected;
        var decidedAt = _timeProvider.GetUtcNow();

        var updatedCount = _dbContext.ReconciliationBatches
            .Where(batch => batch.Id == id &&
                batch.Status == ReconciliationBatchStatus.Completed &&
                batch.ApprovalStatus == ReconciliationApprovalStatus.Pending)
            .ExecuteUpdate(setters => setters
                .SetProperty(batch => batch.ApprovalStatus, approvalStatus)
                .SetProperty(batch => batch.DecisionBy, decisionBy)
                .SetProperty(batch => batch.DecisionAt, decidedAt)
                .SetProperty(batch => batch.DecisionComment, comment));

        if (updatedCount == 1)
        {
            return new ReconciliationApprovalDecisionResult(
                ReconciliationApprovalDecisionOutcome.Updated,
                GetById(id));
        }

        var state = _dbContext.ReconciliationBatches
            .AsNoTracking()
            .Where(batch => batch.Id == id)
            .Select(batch => new { batch.Status, batch.ApprovalStatus })
            .SingleOrDefault();

        if (state is null)
        {
            return new ReconciliationApprovalDecisionResult(
                ReconciliationApprovalDecisionOutcome.NotFound);
        }

        return state.Status != ReconciliationBatchStatus.Completed
            ? new ReconciliationApprovalDecisionResult(
                ReconciliationApprovalDecisionOutcome.BatchNotCompleted)
            : new ReconciliationApprovalDecisionResult(
                ReconciliationApprovalDecisionOutcome.AlreadyDecided);
    }

    private static void ResetApproval(
        ReconciliationBatchEntity entity,
        ReconciliationApprovalStatus approvalStatus)
    {
        entity.ApprovalStatus = approvalStatus;
        entity.DecisionBy = null;
        entity.DecisionAt = null;
        entity.DecisionComment = null;
    }

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

    private ReconciliationBatchEntity GetRequiredEntity(Guid id)
    {
        return _dbContext.ReconciliationBatches.Find(id) ??
            throw new InvalidOperationException($"Reconciliation batch '{id}' was not found.");
    }

    private static void ApplySummary(
        ReconciliationBatchEntity entity,
        ReconciliationSummary summary)
    {
        entity.TotalBranchRecords = summary.TotalBranchRecords;
        entity.TotalBankRecords = summary.TotalBankRecords;
        entity.MatchedCount = summary.MatchedCount;
        entity.OnlyInBranchCount = summary.OnlyInBranchCount;
        entity.OnlyInBankCount = summary.OnlyInBankCount;
        entity.MismatchCount = summary.MismatchCount;
        entity.Differences.Clear();
        entity.Differences.AddRange(summary.Results
            .Where(result => result.Status != ReconciliationStatus.Matched)
            .Select(ToEntity));
    }

    public IReadOnlyCollection<ReconciliationBatch> GetAll(ReconciliationHistoryQuery? query = null)
    {
        query ??= new ReconciliationHistoryQuery();
        var batches = ApplyFilters(_dbContext.ReconciliationBatches.AsNoTracking(), query);

        return batches
            .OrderByDescending(batch => batch.CreatedAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(batch => ToSummaryBatch(batch))
            .ToList();
    }

    public int Count(ReconciliationHistoryQuery? query = null)
    {
        query ??= new ReconciliationHistoryQuery();
        return ApplyFilters(_dbContext.ReconciliationBatches.AsNoTracking(), query).Count();
    }

    private static IQueryable<ReconciliationBatchEntity> ApplyFilters(
        IQueryable<ReconciliationBatchEntity> batches,
        ReconciliationHistoryQuery query)
    {
        if (query.From is not null)
        {
            batches = batches.Where(batch => batch.CreatedAt >= query.From);
        }

        if (query.To is not null)
        {
            batches = batches.Where(batch => batch.CreatedAt <= query.To);
        }

        if (query.Status is not null)
        {
            batches = batches.Where(batch => batch.Status == query.Status);
        }

        if (query.InputType is not null)
        {
            batches = batches.Where(batch => batch.InputType == query.InputType);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLikePattern(query.Search.Trim())}%";
            batches = batches.Where(batch =>
                EF.Functions.ILike(batch.BranchFileName, pattern, "\\") ||
                EF.Functions.ILike(batch.BankFileName, pattern, "\\") ||
                (batch.ErrorCode != null && EF.Functions.ILike(batch.ErrorCode, pattern, "\\")) ||
                (batch.ErrorMessage != null && EF.Functions.ILike(batch.ErrorMessage, pattern, "\\")));
        }

        return batches;
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    public ReconciliationBatch? GetById(Guid id)
    {
        var batch = _dbContext.ReconciliationBatches
            .AsNoTracking()
            .Include(batch => batch.Differences)
            .FirstOrDefault(batch => batch.Id == id);

        return batch is null ? null : ToDetailBatch(batch);
    }

    private static ReconciliationBatchEntity ToEntity(ReconciliationBatch batch)
    {
        return new ReconciliationBatchEntity
        {
            Id = batch.Id,
            CreatedAt = batch.CreatedAt,
            Status = batch.Status,
            InputType = batch.InputType,
            ApprovalStatus = batch.ApprovalStatus,
            InitiatedBy = batch.InitiatedBy,
            DecisionBy = batch.DecisionBy,
            DecisionAt = batch.DecisionAt,
            DecisionComment = batch.DecisionComment,
            BranchFileName = batch.BranchFileName,
            BankFileName = batch.BankFileName,
            TemporaryStorageKey = batch.TemporaryStorageKey,
            ProcessingDurationMilliseconds = batch.ProcessingDurationMilliseconds,
            AttemptCount = batch.AttemptCount,
            LastAttemptAt = batch.LastAttemptAt,
            NextAttemptAt = batch.NextAttemptAt,
            LeaseExpiresAt = batch.LeaseExpiresAt,
            LeaseOwner = batch.LeaseOwner,
            ErrorCode = batch.ErrorCode,
            ErrorMessage = batch.ErrorMessage,
            TotalBranchRecords = batch.Summary.TotalBranchRecords,
            TotalBankRecords = batch.Summary.TotalBankRecords,
            MatchedCount = batch.Summary.MatchedCount,
            OnlyInBranchCount = batch.Summary.OnlyInBranchCount,
            OnlyInBankCount = batch.Summary.OnlyInBankCount,
            MismatchCount = batch.Summary.MismatchCount,
            Differences = batch.Summary.Results
                .Where(result => result.Status != ReconciliationStatus.Matched)
                .Select(ToEntity)
                .ToList()
        };
    }

    private static ReconciliationDifferenceEntity ToEntity(ReconciliationResult result)
    {
        return new ReconciliationDifferenceEntity
        {
            Status = result.Status,
            BranchCode = result.BranchCode,
            FundCode = result.FundCode,
            TransactionNumber = result.TransactionNumber,
            BranchTransactionDate = result.BranchRecord?.TransactionDate,
            BranchQuantity = result.BranchRecord?.Quantity,
            BranchAmount = result.BranchRecord?.Amount,
            BankTransactionDate = result.BankRecord?.TransactionDate,
            BankQuantity = result.BankRecord?.Quantity,
            BankAmount = result.BankRecord?.Amount,
            QuantityDifference = result.QuantityDifference,
            AmountDifference = result.AmountDifference,
            BranchExtraFieldsJson = SerializeDictionary(result.BranchRecord?.ExtraFields),
            BankExtraFieldsJson = SerializeDictionary(result.BankRecord?.ExtraFields),
            FieldDifferencesJson = SerializeDictionary(result.FieldDifferences)
        };
    }

    private static ReconciliationBatch ToSummaryBatch(ReconciliationBatchEntity entity)
    {
        return new ReconciliationBatch
        {
            Id = entity.Id,
            CreatedAt = entity.CreatedAt,
            Status = entity.Status,
            InputType = entity.InputType,
            ApprovalStatus = entity.ApprovalStatus,
            InitiatedBy = entity.InitiatedBy,
            DecisionBy = entity.DecisionBy,
            DecisionAt = entity.DecisionAt,
            DecisionComment = entity.DecisionComment,
            BranchFileName = entity.BranchFileName,
            BankFileName = entity.BankFileName,
            TemporaryStorageKey = entity.TemporaryStorageKey,
            ProcessingDurationMilliseconds = entity.ProcessingDurationMilliseconds,
            AttemptCount = entity.AttemptCount,
            LastAttemptAt = entity.LastAttemptAt,
            NextAttemptAt = entity.NextAttemptAt,
            LeaseExpiresAt = entity.LeaseExpiresAt,
            LeaseOwner = entity.LeaseOwner,
            ErrorCode = entity.ErrorCode,
            ErrorMessage = entity.ErrorMessage,
            Summary = ToSummary(entity, [])
        };
    }

    private static ReconciliationBatch ToDetailBatch(ReconciliationBatchEntity entity)
    {
        return new ReconciliationBatch
        {
            Id = entity.Id,
            CreatedAt = entity.CreatedAt,
            Status = entity.Status,
            InputType = entity.InputType,
            ApprovalStatus = entity.ApprovalStatus,
            InitiatedBy = entity.InitiatedBy,
            DecisionBy = entity.DecisionBy,
            DecisionAt = entity.DecisionAt,
            DecisionComment = entity.DecisionComment,
            BranchFileName = entity.BranchFileName,
            BankFileName = entity.BankFileName,
            TemporaryStorageKey = entity.TemporaryStorageKey,
            ProcessingDurationMilliseconds = entity.ProcessingDurationMilliseconds,
            AttemptCount = entity.AttemptCount,
            LastAttemptAt = entity.LastAttemptAt,
            NextAttemptAt = entity.NextAttemptAt,
            LeaseExpiresAt = entity.LeaseExpiresAt,
            LeaseOwner = entity.LeaseOwner,
            ErrorCode = entity.ErrorCode,
            ErrorMessage = entity.ErrorMessage,
            Summary = ToSummary(
                entity,
                entity.Differences
                    .OrderBy(difference => difference.Id)
                    .Select(ToResult)
                    .ToList())
        };
    }

    private static ReconciliationSummary ToSummary(
        ReconciliationBatchEntity entity,
        List<ReconciliationResult> results)
    {
        return new ReconciliationSummary
        {
            TotalBranchRecords = entity.TotalBranchRecords,
            TotalBankRecords = entity.TotalBankRecords,
            MatchedCount = entity.MatchedCount,
            OnlyInBranchCount = entity.OnlyInBranchCount,
            OnlyInBankCount = entity.OnlyInBankCount,
            MismatchCount = entity.MismatchCount,
            Results = results
        };
    }

    private static ReconciliationResult ToResult(ReconciliationDifferenceEntity entity)
    {
        return new ReconciliationResult
        {
            Status = entity.Status,
            BranchCode = entity.BranchCode,
            FundCode = entity.FundCode,
            TransactionNumber = entity.TransactionNumber,
            BranchRecord = CreateRecord(
                entity.BranchTransactionDate,
                entity.BranchQuantity,
                entity.BranchAmount,
                entity.BranchCode,
                entity.FundCode,
                entity.TransactionNumber,
                DeserializeStringDictionary(entity.BranchExtraFieldsJson)),
            BankRecord = CreateRecord(
                entity.BankTransactionDate,
                entity.BankQuantity,
                entity.BankAmount,
                entity.BranchCode,
                entity.FundCode,
                entity.TransactionNumber,
                DeserializeStringDictionary(entity.BankExtraFieldsJson)),
            QuantityDifference = entity.QuantityDifference,
            AmountDifference = entity.AmountDifference,
            FieldDifferences = DeserializeDecimalDictionary(entity.FieldDifferencesJson),
            FieldValues = CreateDefaultFieldValues(
                entity.BranchCode,
                entity.FundCode,
                entity.TransactionNumber,
                DeserializeStringDictionary(entity.BranchExtraFieldsJson))
        };
    }

    private static TransactionRecord? CreateRecord(
        DateOnly? transactionDate,
        decimal? quantity,
        decimal? amount,
        string branchCode,
        string fundCode,
        string transactionNumber,
        Dictionary<string, string>? extraFields = null)
    {
        if (transactionDate is null || quantity is null || amount is null)
        {
            return null;
        }

        return new TransactionRecord
        {
            BranchCode = branchCode,
            FundCode = fundCode,
            TransactionNumber = transactionNumber,
            TransactionDate = transactionDate.Value,
            Quantity = quantity.Value,
            Amount = amount.Value,
            ExtraFields = extraFields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string? SerializeDictionary<TValue>(IReadOnlyDictionary<string, TValue>? values)
    {
        return values is null || values.Count == 0
            ? null
            : JsonSerializer.Serialize(values);
    }

    private static Dictionary<string, string> DeserializeStringDictionary(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(
                JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [],
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, decimal> DeserializeDecimalDictionary(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, decimal>(
                JsonSerializer.Deserialize<Dictionary<string, decimal>>(json) ?? [],
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> CreateDefaultFieldValues(
        string branchCode,
        string fundCode,
        string transactionNumber,
        IReadOnlyDictionary<string, string> extraFields)
    {
        var fieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BranchCode"] = branchCode,
            ["FundCode"] = fundCode,
            ["TransactionNumber"] = transactionNumber
        };

        foreach (var extraField in extraFields)
        {
            fieldValues[extraField.Key] = extraField.Value;
        }

        return fieldValues;
    }
}
