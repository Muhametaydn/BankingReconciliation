using System.Globalization;
using System.Text;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class S3ReconciliationTemporaryFileStoreTests
{
    [Fact]
    public async Task SaveOpenAndDelete_UseBatchScopedObjectKeysAndStableStorageIdentity()
    {
        var objectClient = new InMemoryObjectClient();
        var options = CreateOptions();
        var store = CreateStore(objectClient, options);
        var sameStorage = CreateStore(new InMemoryObjectClient(), CreateOptions());
        var otherStorage = CreateStore(
            new InMemoryObjectClient(),
            CreateOptions(prefix: "reconciliation/other"));
        var batchId = Guid.NewGuid();

        var branchLength = await store.SaveBranchStreamAsync(
            batchId,
            new MemoryStream(Encoding.UTF8.GetBytes("branch-object")));
        var bankLength = await store.SaveBankStreamAsync(
            batchId,
            new MemoryStream(Encoding.UTF8.GetBytes("bank-object")));

        Assert.Equal(13, branchLength);
        Assert.Equal(11, bankLength);
        Assert.Equal(store.StorageKey, sameStorage.StorageKey);
        Assert.NotEqual(store.StorageKey, otherStorage.StorageKey);
        Assert.Contains(
            $"reconciliation/uploads/{batchId:N}/branch-upload.dat",
            objectClient.Keys);
        Assert.Contains(
            $"reconciliation/uploads/{batchId:N}/bank-upload.dat",
            objectClient.Keys);
        Assert.True(await store.ExistsAsync(batchId));
        await using (var stream = await store.OpenBranchReadAsync(batchId))
        using (var reader = new StreamReader(stream))
        {
            Assert.Equal("branch-object", await reader.ReadToEndAsync());
        }

        Assert.True(await store.DeleteAsync(batchId));
        Assert.False(await store.ExistsAsync(batchId));
    }

    [Fact]
    public async Task SaveStream_RejectsActualBytesOverLimit()
    {
        var objectClient = new InMemoryObjectClient();
        var store = CreateStore(objectClient, CreateOptions(maxFileSizeBytes: 4));
        var batchId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<ReconciliationTemporaryFileLimitException>(() =>
            store.SaveBranchStreamAsync(
                batchId,
                new MemoryStream(Encoding.UTF8.GetBytes("12345"))));

        Assert.Equal(4, exception.MaxFileSizeBytes);
        Assert.Empty(objectClient.Keys);
    }

    [Fact]
    public async Task GetExpiredBatchIds_UsesNewestObjectTimestampForEachBatch()
    {
        var objectClient = new InMemoryObjectClient();
        var store = CreateStore(objectClient, CreateOptions());
        var now = DateTimeOffset.UtcNow;
        var expiredBatchId = Guid.NewGuid();
        var activeByTimestampBatchId = Guid.NewGuid();
        objectClient.Seed(
            $"reconciliation/uploads/{expiredBatchId:N}/branch-upload.dat",
            "branch",
            now.AddHours(-3));
        objectClient.Seed(
            $"reconciliation/uploads/{expiredBatchId:N}/bank-upload.dat",
            "bank",
            now.AddHours(-2));
        objectClient.Seed(
            $"reconciliation/uploads/{activeByTimestampBatchId:N}/branch-upload.dat",
            "branch",
            now.AddHours(-3));
        objectClient.Seed(
            $"reconciliation/uploads/{activeByTimestampBatchId:N}/bank-upload.dat",
            "bank",
            now);
        objectClient.Seed(
            "reconciliation/uploads/not-a-batch/ignored.dat",
            "ignored",
            now.AddHours(-10));

        var expiredBatchIds = await store.GetExpiredBatchIdsAsync(
            now.AddHours(-1),
            take: 10);

        Assert.Equal(expiredBatchId, Assert.Single(expiredBatchIds));
    }

    [Fact]
    public async Task VerifyAvailabilityAsync_UsesOnePrefixScopedListRequest()
    {
        var objectClient = new InMemoryObjectClient();
        var store = CreateStore(objectClient, CreateOptions());

        await store.VerifyAvailabilityAsync();

        Assert.Equal(1, objectClient.ListCallCount);
        Assert.Equal("reconciliation/uploads/", objectClient.LastListPrefix);
        Assert.Equal(1, objectClient.LastListMaxKeys);
    }

    private static S3ReconciliationTemporaryFileStore CreateStore(
        IReconciliationObjectClient objectClient,
        ReconciliationUploadOptions options) =>
        new(objectClient, Options.Create(options));

    private static ReconciliationUploadOptions CreateOptions(
        string prefix = "reconciliation/uploads",
        long maxFileSizeBytes = 1024) =>
        new()
        {
            TemporaryStorageMode = ReconciliationTemporaryStorageMode.S3Compatible,
            MaxCsvFileSizeBytes = maxFileSizeBytes,
            S3BucketName = "reconciliation-tests",
            S3Prefix = prefix,
            S3Region = "us-east-1",
            S3ServiceUrl = "http://minio.test:9000",
            S3ForcePathStyle = true
        };

    private sealed class InMemoryObjectClient : IReconciliationObjectClient
    {
        private readonly Dictionary<string, StoredObject> _objects =
            new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => _objects.Keys;
        public int ListCallCount { get; private set; }
        public string? LastListPrefix { get; private set; }
        public int LastListMaxKeys { get; private set; }

        public async Task PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _objects[key] = new StoredObject(
                buffer.ToArray(),
                DateTimeOffset.UtcNow);
        }

        public Task<Stream> OpenReadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_objects.TryGetValue(key, out var storedObject))
            {
                throw new FileNotFoundException("Object was not found.", key);
            }

            Stream stream = new MemoryStream(storedObject.Content, writable: false);
            return Task.FromResult(stream);
        }

        public Task<bool> ExistsAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_objects.ContainsKey(key));
        }

        public Task DeleteAsync(
            IReadOnlyCollection<string> keys,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var key in keys)
            {
                _objects.Remove(key);
            }

            return Task.CompletedTask;
        }

        public Task<ReconciliationObjectPage> ListAsync(
            string prefix,
            string? continuationToken,
            int maxKeys,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListCallCount++;
            LastListPrefix = prefix;
            LastListMaxKeys = maxKeys;
            var offset = continuationToken is null
                ? 0
                : int.Parse(continuationToken, CultureInfo.InvariantCulture);
            var allObjects = _objects
                .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToList();
            var objects = allObjects
                .Skip(offset)
                .Take(maxKeys)
                .Select(item => new ReconciliationObjectInfo(
                    item.Key,
                    item.Value.LastModified))
                .ToList();
            var nextOffset = offset + objects.Count;
            var nextToken = nextOffset < allObjects.Count
                ? nextOffset.ToString(CultureInfo.InvariantCulture)
                : null;
            return Task.FromResult(new ReconciliationObjectPage(objects, nextToken));
        }

        public void Seed(
            string key,
            string content,
            DateTimeOffset lastModified)
        {
            _objects[key] = new StoredObject(
                Encoding.UTF8.GetBytes(content),
                lastModified);
        }

        private sealed record StoredObject(
            byte[] Content,
            DateTimeOffset LastModified);
    }
}
