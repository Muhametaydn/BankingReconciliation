namespace BankingReconciliation.Api.Services;

public interface IReconciliationReadinessService
{
    Task<ReconciliationReadinessResult> CheckAsync(
        CancellationToken cancellationToken = default);
}

public sealed record ReconciliationReadinessResult(
    bool DatabaseAvailable,
    bool TemporaryStorageAvailable)
{
    public bool IsReady => DatabaseAvailable && TemporaryStorageAvailable;
}
