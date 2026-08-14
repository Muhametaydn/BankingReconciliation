using System.Security.Cryptography;
using System.Text;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class S3ReconciliationTemporaryFileStore : IReconciliationTemporaryFileStore
{
    private const string BranchFileName = "branch-upload.dat";
    private const string BankFileName = "bank-upload.dat";
    private readonly IReconciliationObjectClient _objectClient;
    private readonly long _maxFileSizeBytes;
    private readonly string _rootPrefix;

    public S3ReconciliationTemporaryFileStore(
        IReconciliationObjectClient objectClient,
        IOptions<ReconciliationUploadOptions> uploadOptions)
    {
        _objectClient = objectClient;
        var options = uploadOptions.Value;
        _maxFileSizeBytes = options.MaxCsvFileSizeBytes;
        _rootPrefix = NormalizePrefix(options.S3Prefix) + "/";
        StorageKey = CreateStorageKey(options);
    }

    public string StorageKey { get; }

    public async Task VerifyAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _objectClient.ListAsync(
                _rootPrefix,
                continuationToken: null,
                maxKeys: 1,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReconciliationTemporaryFileException(
                "S3-compatible reconciliation storage is unavailable.",
                exception);
        }
    }

    public async Task SaveAsync(
        Guid batchId,
        IFormFile branchFile,
        IFormFile bankFile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using (var branchStream = branchFile.OpenReadStream())
            {
                await SaveObjectAsync(
                    branchStream,
                    GetObjectKey(batchId, BranchFileName),
                    cancellationToken);
            }

            await using (var bankStream = bankFile.OpenReadStream())
            {
                await SaveObjectAsync(
                    bankStream,
                    GetObjectKey(batchId, BankFileName),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            await DeleteAsync(batchId, CancellationToken.None);
            throw;
        }
        catch (ReconciliationTemporaryFileException)
        {
            await DeleteAsync(batchId, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await DeleteAsync(batchId, CancellationToken.None);
            throw new ReconciliationTemporaryFileException(
                "Uploaded files could not be stored in object storage for background processing.",
                exception);
        }
    }

    public Task<long> SaveBranchStreamAsync(
        Guid batchId,
        Stream source,
        CancellationToken cancellationToken = default) =>
        SaveBatchStreamAsync(batchId, source, BranchFileName, cancellationToken);

    public Task<long> SaveBankStreamAsync(
        Guid batchId,
        Stream source,
        CancellationToken cancellationToken = default) =>
        SaveBatchStreamAsync(batchId, source, BankFileName, cancellationToken);

    public Task<Stream> OpenBranchReadAsync(
        Guid batchId,
        CancellationToken cancellationToken = default) =>
        OpenReadAsync(batchId, BranchFileName, cancellationToken);

    public Task<Stream> OpenBankReadAsync(
        Guid batchId,
        CancellationToken cancellationToken = default) =>
        OpenReadAsync(batchId, BankFileName, cancellationToken);

    public async Task<bool> ExistsAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _objectClient.ExistsAsync(
                    GetObjectKey(batchId, BranchFileName),
                    cancellationToken) &&
                await _objectClient.ExistsAsync(
                    GetObjectKey(batchId, BankFileName),
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReconciliationTemporaryFileException(
                "Uploaded files could not be checked in object storage.",
                exception);
        }
    }

    public async Task<IReadOnlyCollection<Guid>> GetExpiredBatchIdsAsync(
        DateTimeOffset olderThan,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        var result = new List<Guid>();
        Guid? currentBatchId = null;
        var currentLastModified = DateTimeOffset.MinValue;
        string? continuationToken = null;
        var maxKeys = (int)Math.Min(1000L, Math.Max(100L, (long)take * 4));

        void CompleteCurrentBatch()
        {
            if (currentBatchId is not null &&
                currentLastModified <= olderThan &&
                result.Count < take)
            {
                result.Add(currentBatchId.Value);
            }
        }

        try
        {
            do
            {
                var page = await _objectClient.ListAsync(
                    _rootPrefix,
                    continuationToken,
                    maxKeys,
                    cancellationToken);
                foreach (var item in page.Objects)
                {
                    if (!TryGetBatchId(item.Key, out var batchId))
                    {
                        continue;
                    }

                    if (currentBatchId != batchId)
                    {
                        CompleteCurrentBatch();
                        if (result.Count >= take)
                        {
                            return result;
                        }

                        currentBatchId = batchId;
                        currentLastModified = item.LastModified;
                    }
                    else if (item.LastModified > currentLastModified)
                    {
                        currentLastModified = item.LastModified;
                    }
                }

                if (page.NextContinuationToken is not null &&
                    string.Equals(
                        continuationToken,
                        page.NextContinuationToken,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Object storage returned a repeated continuation token.");
                }

                continuationToken = page.NextContinuationToken;
            }
            while (continuationToken is not null);

            CompleteCurrentBatch();
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReconciliationTemporaryFileException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReconciliationTemporaryFileException(
                "Object storage could not be scanned for expired reconciliation files.",
                exception);
        }
    }

    public async Task<bool> DeleteAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _objectClient.DeleteAsync(
                [
                    GetObjectKey(batchId, BranchFileName),
                    GetObjectKey(batchId, BankFileName)
                ],
                cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<long> SaveBatchStreamAsync(
        Guid batchId,
        Stream source,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SaveObjectAsync(
                source,
                GetObjectKey(batchId, fileName),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReconciliationTemporaryFileException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReconciliationTemporaryFileException(
                "Uploaded files could not be stored in object storage for background processing.",
                exception);
        }
    }

    private async Task<long> SaveObjectAsync(
        Stream source,
        string key,
        CancellationToken cancellationToken)
    {
        var limitedStream = new SizeLimitedReadStream(source, _maxFileSizeBytes);
        var stagingPath = Path.Combine(
            Path.GetTempPath(),
            $"banking-reconciliation-s3-{Guid.NewGuid():N}.tmp");
        try
        {
            await using var stagingStream = new FileStream(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous |
                    FileOptions.SequentialScan |
                    FileOptions.DeleteOnClose);
            await limitedStream.CopyToAsync(stagingStream, cancellationToken);
            await stagingStream.FlushAsync(cancellationToken);
            stagingStream.Position = 0;
            await _objectClient.PutAsync(key, stagingStream, cancellationToken);
            return limitedStream.TotalBytesRead;
        }
        catch (Exception exception)
        {
            var limitException = FindLimitException(exception);
            if (limitException is not null)
            {
                throw limitException;
            }

            throw;
        }
    }

    private async Task<Stream> OpenReadAsync(
        Guid batchId,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _objectClient.OpenReadAsync(
                GetObjectKey(batchId, fileName),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            throw new ReconciliationTemporaryFileException(
                "Uploaded files are no longer available for background processing.",
                exception);
        }
        catch (Exception exception)
        {
            throw new ReconciliationTemporaryFileException(
                "Uploaded files could not be opened from object storage.",
                exception);
        }
    }

    private string GetObjectKey(Guid batchId, string fileName) =>
        $"{_rootPrefix}{batchId:N}/{fileName}";

    private bool TryGetBatchId(string key, out Guid batchId)
    {
        batchId = Guid.Empty;
        if (!key.StartsWith(_rootPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var relativeKey = key[_rootPrefix.Length..];
        var separatorIndex = relativeKey.IndexOf('/');
        return separatorIndex == 32 &&
            Guid.TryParseExact(relativeKey[..separatorIndex], "N", out batchId);
    }

    private static string NormalizePrefix(string prefix) =>
        prefix.Replace('\\', '/').Trim('/');

    private static string CreateStorageKey(ReconciliationUploadOptions options)
    {
        var endpoint = string.IsNullOrWhiteSpace(options.S3ServiceUrl)
            ? $"aws://{options.S3Region.Trim().ToLowerInvariant()}"
            : new Uri(options.S3ServiceUrl.TrimEnd('/'))
                .AbsoluteUri
                .TrimEnd('/')
                .ToLowerInvariant();
        var identity = string.Join(
            '\n',
            "S3Compatible",
            endpoint,
            options.S3BucketName.Trim(),
            NormalizePrefix(options.S3Prefix));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static ReconciliationTemporaryFileLimitException? FindLimitException(
        Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ReconciliationTemporaryFileLimitException limitException)
            {
                return limitException;
            }
        }

        return null;
    }

    private sealed class SizeLimitedReadStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly long _maxBytes;

        public SizeLimitedReadStream(Stream innerStream, long maxBytes)
        {
            _innerStream = innerStream;
            _maxBytes = maxBytes;
        }

        public long TotalBytesRead { get; private set; }
        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = _innerStream.Read(buffer, offset, count);
            RecordRead(bytesRead);
            return bytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var bytesRead = _innerStream.Read(buffer);
            RecordRead(bytesRead);
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken);
            RecordRead(bytesRead);
            return bytesRead;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var bytesRead = await _innerStream.ReadAsync(
                buffer,
                offset,
                count,
                cancellationToken);
            RecordRead(bytesRead);
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // The request or form stream remains owned by the caller.
            base.Dispose(disposing);
        }

        private void RecordRead(int bytesRead)
        {
            TotalBytesRead += bytesRead;
            if (TotalBytesRead > _maxBytes)
            {
                throw new ReconciliationTemporaryFileLimitException(_maxBytes);
            }
        }
    }
}
