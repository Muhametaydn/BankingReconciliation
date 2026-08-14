using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Contracts;

public class ReconciliationResultResponse
{
    public ReconciliationStatus Status { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string FundCode { get; set; } = string.Empty;
    public string TransactionNumber { get; set; } = string.Empty;
    public TransactionRecordResponse? BranchRecord { get; set; }
    public TransactionRecordResponse? BankRecord { get; set; }
    public decimal? QuantityDifference { get; set; }
    public decimal? AmountDifference { get; set; }
    public Dictionary<string, decimal> FieldDifferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FieldValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
