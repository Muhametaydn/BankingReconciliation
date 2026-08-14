namespace BankingReconciliation.Api.Models;

public enum ReconciliationJobFailureDisposition
{
    LeaseLost,
    RetryScheduled,
    Failed
}
