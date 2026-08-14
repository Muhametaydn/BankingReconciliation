namespace BankingReconciliation.Api.Services;

public interface IReconciliationObjectClient
{
    Task PutAsync(
        string key,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default);

    Task<ReconciliationObjectPage> ListAsync(
        string prefix,
        string? continuationToken,
        int maxKeys,
        CancellationToken cancellationToken = default);
}

public sealed record ReconciliationObjectInfo(
    string Key,
    DateTimeOffset LastModified);

public sealed record ReconciliationObjectPage(
    IReadOnlyCollection<ReconciliationObjectInfo> Objects,
    string? NextContinuationToken);
