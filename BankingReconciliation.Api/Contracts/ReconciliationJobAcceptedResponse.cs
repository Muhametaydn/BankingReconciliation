using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Contracts;

public sealed class ReconciliationJobAcceptedResponse
{
    public Guid BatchId { get; set; }
    public ReconciliationBatchStatus Status { get; set; }
    public ReconciliationInputType InputType { get; set; }
}
