namespace BankingReconciliation.Api.Models;

public class ReconciliationSummary
{
    public int TotalBranchRecords { get; set; }
    public int TotalBankRecords { get; set; }
    public int MatchedCount { get; set; }
    public int OnlyInBranchCount { get; set; }
    public int OnlyInBankCount { get; set; }
    public int MismatchCount { get; set; }
    public bool IsExactMatch { get; set; }
    public List<ReconciliationResult> Results { get; set; } = [];
}
