namespace BankingReconciliation.Api.Models;

public enum ReconciliationApprovalDecisionOutcome
{
    Updated,
    NotFound,
    BatchNotCompleted,
    AlreadyDecided
}

public sealed record ReconciliationApprovalDecisionResult(
    ReconciliationApprovalDecisionOutcome Outcome,
    ReconciliationBatch? Batch = null);
