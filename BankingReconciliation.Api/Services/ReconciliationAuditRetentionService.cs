using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationAuditRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ReconciliationAuditRetentionOptions _options;
    private readonly IReconciliationImmutableAuditArchive _immutableArchive;
    private readonly ReconciliationAuditRetentionMonitor _monitor;
    private readonly ILogger<ReconciliationAuditRetentionService> _logger;

    public ReconciliationAuditRetentionService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<ReconciliationAuditRetentionOptions> options,
        IReconciliationImmutableAuditArchive immutableArchive,
        ReconciliationAuditRetentionMonitor monitor,
        ILogger<ReconciliationAuditRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options.Value;
        _immutableArchive = immutableArchive;
        _monitor = monitor;
        _logger = logger;
    }

    public async Task<ReconciliationAuditRetentionResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var startedAt = _timeProvider.GetUtcNow();
        _monitor.MarkStarted(startedAt);
        if (!_options.Enabled)
        {
            _monitor.MarkDisabled(startedAt);
            return new ReconciliationAuditRetentionResult(0, 0);
        }

        var now = startedAt;
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReconciliationAuditRepository>();
        var result = await repository.ArchiveAndPurgeAsync(
            now.AddDays(-_options.HotRetentionDays),
            _options.ArchiveRetentionDays is null
                ? null
                : now.AddDays(-_options.ArchiveRetentionDays.Value),
            _options.BatchSize,
            _immutableArchive.Enabled,
            cancellationToken);

        var externalArchivedCount = 0;
        if (_immutableArchive.Enabled)
        {
            var pending = repository.GetPendingExternalArchive(_options.BatchSize);
            if (pending.Count > 0)
            {
                var objectKey = await _immutableArchive.WriteAsync(pending, cancellationToken);
                repository.MarkExternalArchived(
                    pending.Select(item => item.Id).ToArray(),
                    objectKey,
                    now);
                externalArchivedCount = pending.Count;
            }
        }

        if (result.ArchivedCount > 0 || result.PurgedCount > 0 || externalArchivedCount > 0)
        {
            _logger.LogInformation(
                "Audit retention completed. ArchivedCount={ArchivedCount}, PurgedCount={PurgedCount}, ExternalArchivedCount={ExternalArchivedCount}",
                result.ArchivedCount,
                result.PurgedCount,
                externalArchivedCount);
        }

        _monitor.UpdateStorageStatistics(repository.GetStorageStatistics());

        _monitor.MarkSucceeded(
            _timeProvider.GetUtcNow(),
            result.ArchivedCount,
            result.PurgedCount,
            externalArchivedCount);

        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _monitor.MarkFailed(_timeProvider.GetUtcNow());
                _logger.LogError(exception, "Audit retention failed; processing will continue.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromHours(_options.CleanupIntervalHours),
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
