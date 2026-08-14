using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Contracts;

public class ReconciliationApprovalDecisionRequest
{
    public ReconciliationApprovalDecision Decision { get; set; }
    public string? Comment { get; set; }
}
