using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationReadinessService : IReconciliationReadinessService
{
    private readonly IReconciliationTemporaryFileStore _temporaryFileStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReconciliationReadinessOptions _options;
    private readonly ILogger<ReconciliationReadinessService> _logger;

    public ReconciliationReadinessService(
        IReconciliationTemporaryFileStore temporaryFileStore,
        IServiceScopeFactory scopeFactory,
        IOptions<ReconciliationReadinessOptions> options,
        ILogger<ReconciliationReadinessService> logger)
    {
        _temporaryFileStore = temporaryFileStore;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReconciliationReadinessResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var databaseTask = CheckDatabaseAsync(timeoutCancellation.Token);
        var storageTask = CheckTemporaryStorageAsync(timeoutCancellation.Token);
        await Task.WhenAll(databaseTask, storageTask);
        cancellationToken.ThrowIfCancellationRequested();

        return new ReconciliationReadinessResult(
            await databaseTask,
            await storageTask);
    }

    private async Task<bool> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetService<ReconciliationDbContext>();
            return dbContext is null ||
                await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "PostgreSQL readiness check failed.");
            return false;
        }
    }

    private async Task<bool> CheckTemporaryStorageAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _temporaryFileStore.VerifyAvailabilityAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Temporary storage readiness check failed.");
            return false;
        }
    }
}
