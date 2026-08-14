using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Contracts;

public class ReconciliationSummaryResponse
{
    public Guid? BatchId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public ReconciliationBatchStatus? BatchStatus { get; set; }
    public ReconciliationInputType? InputType { get; set; }
    public ReconciliationApprovalStatus? ApprovalStatus { get; set; }
    public string? InitiatedBy { get; set; }
    public string? DecisionBy { get; set; }
    public DateTimeOffset? DecisionAt { get; set; }
    public string? DecisionComment { get; set; }
    public string? BranchFileName { get; set; }
    public string? BankFileName { get; set; }
    public long? ProcessingDurationMilliseconds { get; set; }
    public int? AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalBranchRecords { get; set; }
    public int TotalBankRecords { get; set; }
    public int MatchedCount { get; set; }
    public int OnlyInBranchCount { get; set; }
    public int OnlyInBankCount { get; set; }
    public int MismatchCount { get; set; }
    public bool IsExactMatch { get; set; }
    public List<ReconciliationResultResponse> Results { get; set; } = [];
}
