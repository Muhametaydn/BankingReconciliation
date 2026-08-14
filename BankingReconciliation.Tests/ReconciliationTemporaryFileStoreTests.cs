using System.Text;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class ReconciliationTemporaryFileStoreTests
{
    [Fact]
    public async Task SaveAsync_StoresFilesUnderBatchIdAndDeletesThem()
    {
        var rootPath = CreateRootPath();
        try
        {
            var store = CreateStore(rootPath, maxFileSizeBytes: 1024);
            var batchId = Guid.NewGuid();
            var branchFile = CreateFile("branch-content", "../branch.csv");
            var bankFile = CreateFile("bank-content", "..\\bank.csv");

            await store.SaveAsync(batchId, branchFile, bankFile);
            Assert.True(await store.ExistsAsync(batchId));
            string branchContent;
            string bankContent;
            await using (var branchStream = await store.OpenBranchReadAsync(batchId))
            await using (var bankStream = await store.OpenBankReadAsync(batchId))
            {
                using var branchReader = new StreamReader(branchStream);
                using var bankReader = new StreamReader(bankStream);
                branchContent = await branchReader.ReadToEndAsync();
                bankContent = await bankReader.ReadToEndAsync();
            }

            Assert.Equal("branch-content", branchContent);
            Assert.Equal("bank-content", bankContent);

            await store.DeleteAsync(batchId);
            Assert.False(await store.ExistsAsync(batchId));
        }
        finally
        {
            DeleteRootPath(rootPath);
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsActualContentOverLimitAndCleansPartialFiles()
    {
        var rootPath = CreateRootPath();
        try
        {
            var store = CreateStore(rootPath, maxFileSizeBytes: 4);
            var batchId = Guid.NewGuid();

            var exception = await Assert.ThrowsAsync<ReconciliationTemporaryFileLimitException>(() => store.SaveAsync(
                batchId,
                CreateFile("12345", "branch.csv"),
                CreateFile("1234", "bank.csv")));

            Assert.Equal(4, exception.MaxFileSizeBytes);
            Assert.False(await store.ExistsAsync(batchId));
            Assert.False(Directory.Exists(Path.Combine(rootPath, batchId.ToString("N"))));
        }
        finally
        {
            DeleteRootPath(rootPath);
        }
    }

    [Fact]
    public async Task StreamMethods_StoreRequestStreamsWithoutFormFiles()
    {
        var rootPath = CreateRootPath();
        try
        {
            var store = CreateStore(rootPath, maxFileSizeBytes: 1024);
            var batchId = Guid.NewGuid();

            var branchLength = await store.SaveBranchStreamAsync(
                batchId,
                new MemoryStream(Encoding.UTF8.GetBytes("branch-stream")));
            var bankLength = await store.SaveBankStreamAsync(
                batchId,
                new MemoryStream(Encoding.UTF8.GetBytes("bank-stream")));

            Assert.Equal(13, branchLength);
            Assert.Equal(11, bankLength);
            Assert.True(await store.ExistsAsync(batchId));
        }
        finally
        {
            DeleteRootPath(rootPath);
        }
    }

    [Fact]
    public void StorageKey_IsStableForTheSameRoot_AndDifferentForAnotherRoot()
    {
        var firstRootPath = CreateRootPath();
        var secondRootPath = CreateRootPath();
        try
        {
            var firstStore = CreateStore(firstRootPath, maxFileSizeBytes: 1024);
            var sameRootStore = CreateStore(firstRootPath, maxFileSizeBytes: 1024);
            var otherRootStore = CreateStore(secondRootPath, maxFileSizeBytes: 1024);

            Assert.Equal(firstStore.StorageKey, sameRootStore.StorageKey);
            Assert.NotEqual(firstStore.StorageKey, otherRootStore.StorageKey);
            Assert.True(Guid.TryParseExact(firstStore.StorageKey, "N", out _));
        }
        finally
        {
            DeleteRootPath(firstRootPath);
            DeleteRootPath(secondRootPath);
        }
    }

    [Fact]
    public void Constructor_RejectsAnInvalidStorageIdentity()
    {
        var rootPath = CreateRootPath();
        try
        {
            Directory.CreateDirectory(rootPath);
            File.WriteAllText(
                Path.Combine(rootPath, ".reconciliation-storage-id"),
                "invalid-storage-key");

            var exception = Assert.Throws<ReconciliationTemporaryFileException>(() =>
                CreateStore(rootPath, maxFileSizeBytes: 1024));

            Assert.Contains("identity is invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRootPath(rootPath);
        }
    }

    [Fact]
    public async Task GetExpiredBatchIds_ReturnsOnlyOldBatchDirectories()
    {
        var rootPath = CreateRootPath();
        try
        {
            var store = CreateStore(rootPath, maxFileSizeBytes: 1024);
            var oldBatchId = Guid.NewGuid();
            var recentBatchId = Guid.NewGuid();
            await SaveStreamsAsync(store, oldBatchId);
            await SaveStreamsAsync(store, recentBatchId);
            var invalidDirectory = Directory.CreateDirectory(
                Path.Combine(rootPath, "not-a-batch"));
            var now = DateTimeOffset.UtcNow;
            SetBatchLastWriteTimeUtc(rootPath, oldBatchId, now.AddHours(-3).UtcDateTime);
            invalidDirectory.LastWriteTimeUtc = now.AddHours(-3).UtcDateTime;

            var expiredBatchIds = await store.GetExpiredBatchIdsAsync(
                now.AddHours(-1),
                take: 10);

            Assert.Equal(oldBatchId, Assert.Single(expiredBatchIds));
            Assert.DoesNotContain(recentBatchId, expiredBatchIds);
        }
        finally
        {
            DeleteRootPath(rootPath);
        }
    }

    [Fact]
    public async Task VerifyAvailabilityAsync_PerformsReadWriteDeleteProbeWithoutLeavingAFile()
    {
        var rootPath = CreateRootPath();
        try
        {
            var store = CreateStore(rootPath, maxFileSizeBytes: 1024);

            await store.VerifyAvailabilityAsync();

            Assert.DoesNotContain(
                Directory.EnumerateFiles(rootPath),
                path => Path.GetFileName(path).StartsWith(
                    ".reconciliation-readiness-",
                    StringComparison.Ordinal));
        }
        finally
        {
            DeleteRootPath(rootPath);
        }
    }

    private static ReconciliationTemporaryFileStore CreateStore(string rootPath, long maxFileSizeBytes) =>
        new(Options.Create(new ReconciliationUploadOptions
        {
            TemporaryStoragePath = rootPath,
            MaxCsvFileSizeBytes = maxFileSizeBytes
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

    private static IFormFile CreateFile(string content, string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
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
}
