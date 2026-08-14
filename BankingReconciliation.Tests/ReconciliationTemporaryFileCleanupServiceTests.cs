using System.Text;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class ReconciliationTemporaryFileCleanupServiceTests
{
    [Fact]
    public async Task CleanupOnceAsync_ProtectsActiveJobsAndDeletesExpiredOrTerminalFiles()
    {
        var rootPath = CreateRootPath();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var timeProvider = new MutableTimeProvider(now);
            var store = CreateStore(rootPath);
            var repository = new InMemoryReconciliationHistoryRepository(timeProvider);
            var activeBatchId = Guid.NewGuid();
            var orphanBatchId = Guid.NewGuid();
            var completedBatchId = Guid.NewGuid();
            var recentOrphanBatchId = Guid.NewGuid();
            await SaveStreamsAsync(store, activeBatchId);
            await SaveStreamsAsync(store, orphanBatchId);
            await SaveStreamsAsync(store, completedBatchId);
            await SaveStreamsAsync(store, recentOrphanBatchId);
            repository.AddQueued(
                "active-branch.csv",
                "active-bank.csv",
                ReconciliationInputType.UploadedFiles,
                activeBatchId,
                store.StorageKey);
            repository.AddQueued(
                "completed-branch.csv",
                "completed-bank.csv",
                ReconciliationInputType.UploadedFiles,
                completedBatchId,
                store.StorageKey);
            repository.MarkProcessing(completedBatchId);
            repository.Complete(
                completedBatchId,
                processingDurationMilliseconds: 10,
                new ReconciliationSummary());
            SetBatchLastWriteTimeUtc(rootPath, activeBatchId, now.AddHours(-3).UtcDateTime);
            SetBatchLastWriteTimeUtc(rootPath, orphanBatchId, now.AddHours(-3).UtcDateTime);
            SetBatchLastWriteTimeUtc(rootPath, completedBatchId, now.AddHours(-3).UtcDateTime);

            using var services = new ServiceCollection()
                .AddSingleton<IReconciliationHistoryRepository>(repository)
                .BuildServiceProvider();
            using var cleanupService = new ReconciliationTemporaryFileCleanupService(
                store,
                services.GetRequiredService<IServiceScopeFactory>(),
                timeProvider,
                Options.Create(new ReconciliationUploadOptions
                {
                    TemporaryFileRetentionHours = 1,
                    TemporaryFileCleanupBatchSize = 100
                }),
                NullLogger<ReconciliationTemporaryFileCleanupService>.Instance);

            var cleanupCount = await cleanupService.CleanupOnceAsync();

            Assert.Equal(2, cleanupCount);
            Assert.True(await store.ExistsAsync(activeBatchId));
            Assert.False(await store.ExistsAsync(orphanBatchId));
            Assert.False(await store.ExistsAsync(completedBatchId));
            Assert.True(await store.ExistsAsync(recentOrphanBatchId));
        }
        finally
        {
            DeleteRootPath(rootPath);
        }
    }

    private static ReconciliationTemporaryFileStore CreateStore(string rootPath) =>
        new(Options.Create(new ReconciliationUploadOptions
        {
            TemporaryStoragePath = rootPath,
            MaxCsvFileSizeBytes = 1024
        }));

    private static async Task SaveStreamsAsync(
        ReconciliationTemporaryFileStore store,
        Guid batchId)
    {
        await store.SaveBranchStreamAsync(
            batchId,
            new MemoryStream(Encoding.UTF8.GetBytes("branch")));
        await store.SaveBankStreamAsync(
            batchId,
            new MemoryStream(Encoding.UTF8.GetBytes("bank")));
    }

    private static void SetBatchLastWriteTimeUtc(
        string rootPath,
        Guid batchId,
        DateTime lastWriteTimeUtc)
    {
        var batchPath = Path.Combine(rootPath, batchId.ToString("N"));
        foreach (var filePath in Directory.EnumerateFiles(batchPath))
        {
            File.SetLastWriteTimeUtc(filePath, lastWriteTimeUtc);
        }

        Directory.SetLastWriteTimeUtc(batchPath, lastWriteTimeUtc);
    }

    private static string CreateRootPath() => Path.Combine(
        Path.GetTempPath(),
        "BankingReconciliation.Tests",
        Guid.NewGuid().ToString("N"));

    private static void DeleteRootPath(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
