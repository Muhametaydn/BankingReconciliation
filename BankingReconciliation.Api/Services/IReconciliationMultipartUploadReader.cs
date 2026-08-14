namespace BankingReconciliation.Api.Services;

public interface IReconciliationMultipartUploadReader
{
    Task<ReconciliationStreamedUpload> ReadAsync(
        HttpRequest request,
        Guid batchId,
        CancellationToken cancellationToken = default);
}
