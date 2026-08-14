namespace BankingReconciliation.Api.Models;

public class ReconciliationResult
{
    public ReconciliationStatus Status { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string FundCode { get; set; } = string.Empty;
    public string TransactionNumber { get; set; } = string.Empty;
    public TransactionRecord? BranchRecord { get; set; }
    public TransactionRecord? BankRecord { get; set; }
    public decimal? QuantityDifference { get; set; }
    public decimal? AmountDifference { get; set; }
    public Dictionary<string, decimal> FieldDifferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FieldValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
