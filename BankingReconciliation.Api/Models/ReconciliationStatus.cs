namespace BankingReconciliation.Api.Models;

public enum ReconciliationStatus
{
    Matched,
    OnlyInBranch,
    OnlyInBank,
    QuantityMismatch,
    AmountMismatch,
    QuantityAndAmountMismatch,
    FieldMismatch
}
