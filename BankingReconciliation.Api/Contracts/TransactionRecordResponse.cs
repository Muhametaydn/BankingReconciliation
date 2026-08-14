namespace BankingReconciliation.Api.Contracts;

public class TransactionRecordResponse
{
    public string BranchCode { get; set; } = string.Empty;
    public string FundCode { get; set; } = string.Empty;
    public string TransactionNumber { get; set; } = string.Empty;
    public DateOnly TransactionDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public Dictionary<string, string> ExtraFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
