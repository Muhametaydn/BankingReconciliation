using System.Diagnostics;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationFileJobWorker : BackgroundService
{
    private const int ClaimBatchSize = 100;
    private readonly ReconciliationFileJobQueue _queue;
    private readonly IReconciliationTemporaryFileStore _fileStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ReconciliationJobOptions _options;
    private readonly ILogger<ReconciliationFileJobWorker> _logger;

    public ReconciliationFileJobWorker(
        ReconciliationFileJobQueue queue,
        IReconciliationTemporaryFileStore fileStore,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<ReconciliationJobOptions> options,
        ILogger<ReconciliationFileJobWorker> logger)
    {
        _queue = queue;
        _fileStore = fileStore;
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
                _logger.LogError(exception, "File job polling failed; polling will continue.");
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
            ReconciliationInputType.UploadedFiles,
            _timeProvider.GetUtcNow(),
            ClaimBatchSize,
            _fileStore.StorageKey))
        {
            candidates.Add(persistedId);
        }

        return candidates;
    }

    private async Task ProcessAsync(Guid batchId, CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var historyRepository = scope.ServiceProvider.GetRequiredService<IReconciliationHistoryRepository>();
        var parser = scope.ServiceProvider.GetRequiredService<ITransactionFileParser>();
        var reconciliationService = scope.ServiceProvider.GetRequiredService<IReconciliationService>();
        var leaseOwner = CreateLeaseOwner(batchId);
        var leaseDuration = TimeSpan.FromSeconds(_options.LeaseDurationSeconds);

        if (!historyRepository.TryClaimJob(
            batchId,
            ReconciliationInputType.UploadedFiles,
            leaseOwner,
            _timeProvider.GetUtcNow(),
            leaseDuration,
            _fileStore.StorageKey))
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
        var deleteFiles = false;

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
            if (!await _fileStore.ExistsAsync(batchId, processingCancellation.Token))
            {
                throw new ReconciliationTemporaryFileException(
                    $"Uploaded files for reconciliation batch '{batchId}' are no longer available.");
            }

            await using var branchStream = await _fileStore.OpenBranchReadAsync(
                batchId,
                processingCancellation.Token);
            await using var bankStream = await _fileStore.OpenBankReadAsync(
                batchId,
                processingCancellation.Token);
            var branchRecords = await parser.ParseAsync(branchStream, processingCancellation.Token);
            var bankRecords = await parser.ParseAsync(bankStream, processingCancellation.Token);
            var summary = reconciliationService.Compare(branchRecords, bankRecords);
            stopwatch.Stop();
            await StopHeartbeatAsync();

            if (!historyRepository.TryCompleteClaimedJob(
                batchId,
                leaseOwner,
                stopwatch.ElapsedMilliseconds,
                summary))
            {
                _logger.LogWarning(
                    "File reconciliation lease was lost before completion. BatchId={BatchId}",
                    batchId);
                return;
            }

            deleteFiles = true;
            _logger.LogInformation(
                "Completed background file reconciliation. BatchId={BatchId}, DurationMs={DurationMs}",
                batchId,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            await StopHeartbeatAsync();
            var disposition = HandleFailure(
                historyRepository,
                batchId,
                leaseOwner,
                stopwatch.ElapsedMilliseconds,
                "BackgroundJobInterrupted",
                "Background file reconciliation was interrupted while the application was stopping.",
                retryable: true);
            deleteFiles = disposition == ReconciliationJobFailureDisposition.Failed;
            _logger.LogInformation("Background file reconciliation stopped. BatchId={BatchId}", batchId);
        }
        catch (OperationCanceledException) when (leaseLostCancellation.IsCancellationRequested)
        {
            stopwatch.Stop();
            await StopHeartbeatAsync();
            _logger.LogWarning(
                "File reconciliation stopped because its lease was lost. BatchId={BatchId}",
                batchId);
        }
        catch (CsvTransactionFileParseException exception)
        {
            stopwatch.Stop();
            await StopHeartbeatAsync();
            var disposition = HandleFailure(
                historyRepository,
                batchId,
                leaseOwner,
                stopwatch.ElapsedMilliseconds,
                "InvalidCsvFile",
                exception.Message,
                retryable: false);
            deleteFiles = disposition == ReconciliationJobFailureDisposition.Failed;
            _logger.LogWarning(exception, "Background file validation failed. BatchId={BatchId}", batchId);
        }
        catch (DuplicateTransactionKeyException exception)
        {
            stopwatch.Stop();
            await StopHeartbeatAsync();
            var disposition = HandleFailure(
                historyRepository,
                batchId,
                leaseOwner,
                stopwatch.ElapsedMilliseconds,
                "DuplicateTransactionKey",
                exception.Message,
                retryable: false);
            deleteFiles = disposition == ReconciliationJobFailureDisposition.Failed;
            _logger.LogWarning(exception, "Background file reconciliation found a duplicate key. BatchId={BatchId}", batchId);
        }
        catch (ReconciliationTemporaryFileException exception)
        {
            stopwatch.Stop();
            await StopHeartbeatAsync();
            var disposition = HandleFailure(
                historyRepository,
                batchId,
                leaseOwner,
                stopwatch.ElapsedMilliseconds,
                "UploadedFileUnavailable",
                exception.Message,
                retryable: false);
            deleteFiles = disposition == ReconciliationJobFailureDisposition.Failed;
            _logger.LogWarning(exception, "Background uploaded files are unavailable. BatchId={BatchId}", batchId);
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
                "Background file reconciliation could not be completed.",
                retryable: true);
            deleteFiles = disposition == ReconciliationJobFailureDisposition.Failed;
            _logger.LogError(
                exception,
                "Background file reconciliation failed. BatchId={BatchId}, Disposition={Disposition}",
                batchId,
                disposition);
        }
        finally
        {
            if (deleteFiles)
            {
                if (!await _fileStore.DeleteAsync(batchId, CancellationToken.None))
                {
                    _logger.LogWarning(
                        "Temporary uploaded files could not be deleted and will be retried by retention cleanup. BatchId={BatchId}",
                        batchId);
                }
            }
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
                _logger.LogError(exception, "File job lease renewal failed. BatchId={BatchId}", batchId);
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
