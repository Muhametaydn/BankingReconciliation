using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Data;

public class ReconciliationDifferenceEntity
{
    public long Id { get; set; }
    public Guid BatchId { get; set; }
    public ReconciliationBatchEntity Batch { get; set; } = new();
    public ReconciliationStatus Status { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string FundCode { get; set; } = string.Empty;
    public string TransactionNumber { get; set; } = string.Empty;
    public DateOnly? BranchTransactionDate { get; set; }
    public decimal? BranchQuantity { get; set; }
    public decimal? BranchAmount { get; set; }
    public DateOnly? BankTransactionDate { get; set; }
    public decimal? BankQuantity { get; set; }
    public decimal? BankAmount { get; set; }
    public decimal? QuantityDifference { get; set; }
    public decimal? AmountDifference { get; set; }
    public string? BranchExtraFieldsJson { get; set; }
    public string? BankExtraFieldsJson { get; set; }
    public string? FieldDifferencesJson { get; set; }
}
