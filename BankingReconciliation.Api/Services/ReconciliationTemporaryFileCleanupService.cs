using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationTemporaryFileCleanupService : BackgroundService
{
    private readonly IReconciliationTemporaryFileStore _fileStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ReconciliationUploadOptions _options;
    private readonly ILogger<ReconciliationTemporaryFileCleanupService> _logger;

    public ReconciliationTemporaryFileCleanupService(
        IReconciliationTemporaryFileStore fileStore,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<ReconciliationUploadOptions> options,
        ILogger<ReconciliationTemporaryFileCleanupService> logger)
    {
        _fileStore = fileStore;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> CleanupOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expiredBatchIds = await _fileStore.GetExpiredBatchIdsAsync(
            _timeProvider.GetUtcNow().AddHours(-_options.TemporaryFileRetentionHours),
            _options.TemporaryFileCleanupBatchSize,
            cancellationToken);
        if (expiredBatchIds.Count == 0)
        {
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var historyRepository = scope.ServiceProvider
            .GetRequiredService<IReconciliationHistoryRepository>();
        var activeBatchIds = historyRepository
            .GetActiveUploadedFileJobIds(_fileStore.StorageKey, expiredBatchIds)
            .ToHashSet();
        var cleanupCount = 0;
        foreach (var batchId in expiredBatchIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (activeBatchIds.Contains(batchId))
            {
                continue;
            }

            if (await _fileStore.DeleteAsync(batchId, cancellationToken))
            {
                cleanupCount++;
            }
            else
            {
                _logger.LogWarning(
                    "Expired temporary reconciliation batch could not be deleted and will be retried. BatchId={BatchId}, StorageKey={StorageKey}",
                    batchId,
                    _fileStore.StorageKey);
            }
        }

        if (cleanupCount > 0)
        {
            _logger.LogInformation(
                "Expired temporary reconciliation batches were cleaned. StorageKey={StorageKey}, CleanupCount={CleanupCount}, ProtectedCount={ProtectedCount}",
                _fileStore.StorageKey,
                cleanupCount,
                activeBatchIds.Count);
        }

        return cleanupCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Temporary reconciliation file cleanup failed; cleanup will continue.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(_options.TemporaryFileCleanupIntervalMinutes),
                    _timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
