using System.Diagnostics;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationDatabaseJobWorker : BackgroundService
{
    private const int ClaimBatchSize = 100;
    private readonly ReconciliationDatabaseJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ReconciliationJobOptions _options;
    private readonly ILogger<ReconciliationDatabaseJobWorker> _logger;

    public ReconciliationDatabaseJobWorker(
        ReconciliationDatabaseJobQueue queue,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<ReconciliationJobOptions> options,
        ILogger<ReconciliationDatabaseJobWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var candidates = GetCandidateJobIds();
                foreach (var batchId in candidates)
                {
                    await ProcessAsync(batchId, stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

                if (candidates.Count == 0)
                {
                    await Task.Delay(_options.PollIntervalMilliseconds, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Database job polling failed; polling will continue.");
                await Task.Delay(_options.PollIntervalMilliseconds, stoppingToken);
            }
        }
    }

    private IReadOnlyCollection<Guid> GetCandidateJobIds()
    {
        var candidates = new HashSet<Guid>();
        while (_queue.TryDequeue(out var batchId))
        {
            candidates.Add(batchId);
        }

        using var scope = _scopeFactory.CreateScope();
        var historyRepository = scope.ServiceProvider.GetRequiredService<IReconciliationHistoryRepository>();
        foreach (var persistedId in historyRepository.GetClaimableJobIds(
            ReconciliationInputType.DatabaseSources,
            _timeProvider.GetUtcNow(),
            ClaimBatchSize))
        {
            candidates.Add(persistedId);
        }

        return candidates;
    }

    private async Task ProcessAsync(Guid batchId, CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var historyRepository = scope.ServiceProvider.GetRequiredService<IReconciliationHistoryRepository>();
        var databaseSourceReader = scope.ServiceProvider.GetRequiredService<IReconciliationDatabaseSourceReader>();
        var reconciliationService = scope.ServiceProvider.GetRequiredService<IReconciliationService>();
        var leaseOwner = CreateLeaseOwner(batchId);
        var leaseDuration = TimeSpan.FromSeconds(_options.LeaseDurationSeconds);

        if (!historyRepository.TryClaimJob(
            batchId,
            ReconciliationInputType.DatabaseSources,
            leaseOwner,
            _timeProvider.GetUtcNow(),
            leaseDuration))
        {
            return;
        }

        using var leaseLostCancellation = new CancellationTokenSource();
        using var processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            leaseLostCancellation.Token);
        using var heartbeatCancellation = new CancellationTokenSource();
        var heartbeatTask = RenewLeaseAsync(
            batchId,
            leaseOwner,
            leaseDuration,
            leaseLostCancellation,
            heartbeatCancellation.Token);
        var stopwatch = Stopwatch.StartNew();

        async Task StopHeartbeatAsync()
        {
            heartbeatCancellation.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException) when (heartbeatCancellation.IsCancellationRequested)
            {
            }
        }

        try
        {
            var branchReadTask = databaseSourceReader.ReadAsync("BRANCH", processingCancellation.Token);
            var bankReadTask = databaseSourceReader.ReadAsync("BANK", processingCancellation.Token);
            await Task.WhenAll(branchReadTask, bankReadTask);
            var summary = reconciliationService.Compare(await branchReadTask, await bankReadTask);
            stopwatch.Stop();
            await StopHeartbeatAsync();

            if (!historyRepository.TryCompleteClaimedJob(
                batchId,
                leaseOwner,
                stopwatch.ElapsedMilliseconds,
                summary))
            {
                _logger.LogWarning(
                    "Database reconciliation lease was lost before completion. BatchId={BatchId}",
                    batchId);
                return;
            }

            _logger.LogInformation(
                "Completed background database reconciliation. BatchId={BatchId}, DurationMs={DurationMs}",
                batchId,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            await StopHeartbeatAsync();
            HandleFailure(
                historyRepository,
                batchId,
                leaseOwner,
                stopwatch.ElapsedMilliseconds,
                "BackgroundJobInterrupted",
                "Background reconciliation was interrupted while the application was stopping.",
                retryable: true);
            _logger.LogInformation("Background database reconciliation stopped. BatchId={BatchId}", batchId);
        }
        catch (OperationCanceledException) when (leaseLostCancellation.IsCancellationRequested)
        {
            stopwatch.Stop();
            await StopHeartbeatAsync();
            _logger.LogWarning(
                "Database reconciliation stopped because its lease was lost. BatchId={BatchId}",
                batchId);
        }
        catch (ReconciliationDatabaseSourceException exception)
        {
            stopwatch.Stop();
            await StopHeartbeatAsync();
            var disposition = HandleFailure(
                historyRepository,
                batchId,
                leaseOwner,
                stopwatch.ElapsedMilliseconds,
                "DatabaseSourceReadFailed",
                exception.Message,
                retryable: true);
            _logger.LogWarning(
                exception,
                "Background database source read failed. BatchId={BatchId}, Disposition={Disposition}",
                batchId,
                disposition);
        }
        catch (DuplicateTransactionKeyException exception)
        {
            stopwatch.Stop();
            await StopHeartbeatAsync();
            HandleFailure(
                historyRepository,
                batchId,
                leaseOwner,
                stopwatch.ElapsedMilliseconds,
                "DuplicateTransactionKey",
                exception.Message,
                retryable: false);
            _logger.LogWarning(exception, "Background reconciliation found a duplicate key. BatchId={BatchId}", batchId);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            await StopHeartbeatAsync();
            var disposition = HandleFailure(
                historyRepository,
                batchId,
                leaseOwner,
                stopwatch.ElapsedMilliseconds,
                "BackgroundJobFailed",
                "Background reconciliation could not be completed.",
                retryable: true);
            _logger.LogError(
                exception,
                "Background database reconciliation failed. BatchId={BatchId}, Disposition={Disposition}",
                batchId,
                disposition);
        }
    }

    private ReconciliationJobFailureDisposition HandleFailure(
        IReconciliationHistoryRepository historyRepository,
        Guid batchId,
        string leaseOwner,
        long durationMilliseconds,
        string errorCode,
        string errorMessage,
        bool retryable)
    {
        return historyRepository.HandleClaimedJobFailure(
            batchId,
            leaseOwner,
            durationMilliseconds,
            errorCode,
            errorMessage,
            retryable,
            _options.MaxAttempts,
            _timeProvider.GetUtcNow().AddSeconds(_options.RetryDelaySeconds));
    }

    private async Task RenewLeaseAsync(
        Guid batchId,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationTokenSource leaseLostCancellation,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.LeaseRenewalSeconds), cancellationToken);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var historyRepository = scope.ServiceProvider.GetRequiredService<IReconciliationHistoryRepository>();
                if (!historyRepository.RenewJobLease(
                    batchId,
                    leaseOwner,
                    _timeProvider.GetUtcNow(),
                    leaseDuration))
                {
                    leaseLostCancellation.Cancel();
                    return;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Database job lease renewal failed. BatchId={BatchId}", batchId);
                leaseLostCancellation.Cancel();
                return;
            }
        }
    }

    private static string CreateLeaseOwner(Guid batchId)
    {
        return $"{Environment.MachineName}:{Environment.ProcessId}:{batchId:N}:{Guid.NewGuid():N}";
    }
}
