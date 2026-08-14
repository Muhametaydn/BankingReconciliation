namespace BankingReconciliation.Api.Services;

public interface IReconciliationTemporaryFileStore
{
    string StorageKey { get; }

    Task VerifyAvailabilityAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        Guid batchId,
        IFormFile branchFile,
        IFormFile bankFile,
        CancellationToken cancellationToken = default);

    Task<long> SaveBranchStreamAsync(
        Guid batchId,
        Stream source,
        CancellationToken cancellationToken = default);

    Task<long> SaveBankStreamAsync(
        Guid batchId,
        Stream source,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenBranchReadAsync(
        Guid batchId,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenBankReadAsync(
        Guid batchId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid batchId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetExpiredBatchIdsAsync(
        DateTimeOffset olderThan,
        int take,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid batchId,
        CancellationToken cancellationToken = default);
}
