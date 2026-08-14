namespace BankingReconciliation.Api.Contracts;

public class ReconciliationBatchResponse : ReconciliationBatchListItemResponse
{
    public List<ReconciliationResultResponse> Results { get; set; } = [];
}
