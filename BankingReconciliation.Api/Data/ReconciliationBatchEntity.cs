using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Data;

public class ReconciliationBatchEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ReconciliationBatchStatus Status { get; set; } = ReconciliationBatchStatus.Completed;
    public ReconciliationInputType InputType { get; set; } = ReconciliationInputType.UploadedFiles;
    public ReconciliationApprovalStatus ApprovalStatus { get; set; } = ReconciliationApprovalStatus.Pending;
    public string? InitiatedBy { get; set; }
    public string? DecisionBy { get; set; }
    public DateTimeOffset? DecisionAt { get; set; }
    public string? DecisionComment { get; set; }
    public string BranchFileName { get; set; } = string.Empty;
    public string BankFileName { get; set; } = string.Empty;
    public string? TemporaryStorageKey { get; set; }
    public long ProcessingDurationMilliseconds { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? LeaseOwner { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalBranchRecords { get; set; }
    public int TotalBankRecords { get; set; }
    public int MatchedCount { get; set; }
    public int OnlyInBranchCount { get; set; }
    public int OnlyInBankCount { get; set; }
    public int MismatchCount { get; set; }
    public List<ReconciliationDifferenceEntity> Differences { get; set; } = [];
}
