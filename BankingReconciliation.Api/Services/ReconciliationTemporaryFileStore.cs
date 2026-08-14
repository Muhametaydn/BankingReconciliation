using System.Buffers;
using System.Text;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationTemporaryFileStore : IReconciliationTemporaryFileStore
{
    private const string BranchFileName = "branch-upload.dat";
    private const string BankFileName = "bank-upload.dat";
    private const string StorageIdentityFileName = ".reconciliation-storage-id";
    private readonly string _rootPath;
    private readonly long _maxFileSizeBytes;

    public ReconciliationTemporaryFileStore(IOptions<ReconciliationUploadOptions> uploadOptions)
    {
        var options = uploadOptions.Value;
        _maxFileSizeBytes = options.MaxCsvFileSizeBytes;
        _rootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(options.TemporaryStoragePath)
            ? Path.Combine(Path.GetTempPath(), "BankingReconciliation", "uploads")
            : options.TemporaryStoragePath);
        StorageKey = GetOrCreateStorageKey(_rootPath);
    }

    public string StorageKey { get; }

    public async Task VerifyAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var probePath = Path.Combine(
            _rootPath,
            $".reconciliation-readiness-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(_rootPath);
            await using (var writeStream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await writeStream.WriteAsync(
                    new byte[] { 0x42 },
                    cancellationToken);
                await writeStream.FlushAsync(cancellationToken);
            }

            await using (var readStream = new FileStream(
                probePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var probe = new byte[1];
                var bytesRead = await readStream.ReadAsync(probe, cancellationToken);
                if (bytesRead != 1 || probe[0] != 0x42)
                {
                    throw new IOException(
                        "Temporary reconciliation storage readiness probe could not be read back.");
                }
            }

            File.Delete(probePath);
        }
        catch (OperationCanceledException)
        {
            TryDeleteProbe(probePath);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteProbe(probePath);
            throw new ReconciliationTemporaryFileException(
                "Temporary reconciliation storage is unavailable.",
                exception);
        }
    }

    public async Task SaveAsync(
        Guid batchId,
        IFormFile branchFile,
        IFormFile bankFile,
        CancellationToken cancellationToken = default)
    {
        var directoryPath = GetBatchDirectory(batchId);

        try
        {
            Directory.CreateDirectory(_rootPath);
            Directory.CreateDirectory(directoryPath);
            await using (var branchStream = branchFile.OpenReadStream())
            {
                await SaveStreamAsync(
                    branchStream,
                    GetFilePath(batchId, BranchFileName),
                    cancellationToken);
            }
            await using (var bankStream = bankFile.OpenReadStream())
            {
                await SaveStreamAsync(
                    bankStream,
                    GetFilePath(batchId, BankFileName),
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await DeleteAsync(batchId, CancellationToken.None);
            throw new ReconciliationTemporaryFileException(
                "Uploaded files could not be stored for background processing.",
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

    public Task<bool> ExistsAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            File.Exists(GetFilePath(batchId, BranchFileName)) &&
            File.Exists(GetFilePath(batchId, BankFileName)));
    }

    public Task<IReadOnlyCollection<Guid>> GetExpiredBatchIdsAsync(
        DateTimeOffset olderThan,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_rootPath))
        {
            return Task.FromResult<IReadOnlyCollection<Guid>>([]);
        }

        try
        {
            var expiredBatches = new List<(Guid BatchId, DateTime LastWriteTimeUtc)>();
            foreach (var directory in new DirectoryInfo(_rootPath).EnumerateDirectories())
            {
                try
                {
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        !Guid.TryParseExact(directory.Name, "N", out var batchId))
                    {
                        continue;
                    }

                    var lastWriteTimeUtc = GetLastWriteTimeUtc(directory);
                    if (lastWriteTimeUtc <= olderThan.UtcDateTime)
                    {
                        expiredBatches.Add((batchId, lastWriteTimeUtc));
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // A concurrently deleted or inaccessible directory can be retried later.
                }
            }

            IReadOnlyCollection<Guid> result = expiredBatches
                .OrderBy(item => item.LastWriteTimeUtc)
                .Take(take)
                .Select(item => item.BatchId)
                .ToList();
            return Task.FromResult(result);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ReconciliationTemporaryFileException(
                "Temporary reconciliation storage could not be scanned for expired files.",
                exception);
        }
    }

    public Task<bool> DeleteAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directoryPath = GetBatchDirectory(batchId);
        if (!Directory.Exists(directoryPath))
        {
            return Task.FromResult(true);
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
            return Task.FromResult(true);
        }
        catch (IOException)
        {
            // A best-effort retry on the next startup is safer than failing a completed job.
            return Task.FromResult(false);
        }
        catch (UnauthorizedAccessException)
        {
            // Do not turn a reconciliation result into a failure because cleanup was denied.
            return Task.FromResult(false);
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
            Directory.CreateDirectory(_rootPath);
            Directory.CreateDirectory(GetBatchDirectory(batchId));
            return await SaveStreamAsync(
                source,
                GetFilePath(batchId, fileName),
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ReconciliationTemporaryFileException(
                "Uploaded files could not be stored for background processing.",
                exception);
        }
    }

    private async Task<long> SaveStreamAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var destinationStream = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long totalBytes = 0;

        try
        {
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytes += bytesRead;
                if (totalBytes > _maxFileSizeBytes)
                {
                    throw new ReconciliationTemporaryFileLimitException(_maxFileSizeBytes);
                }

                await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            await destinationStream.FlushAsync(cancellationToken);
            return totalBytes;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private Task<Stream> OpenReadAsync(
        Guid batchId,
        string fileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetFilePath(batchId, fileName);
        if (!File.Exists(path))
        {
            throw new ReconciliationTemporaryFileException(
                "Uploaded files are no longer available for background processing.");
        }

        try
        {
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ReconciliationTemporaryFileException(
                "Uploaded files could not be opened for background processing.",
                exception);
        }
    }

    private string GetFilePath(Guid batchId, string fileName) =>
        Path.Combine(GetBatchDirectory(batchId), fileName);

    private static DateTime GetLastWriteTimeUtc(DirectoryInfo directory)
    {
        directory.Refresh();
        var lastWriteTimeUtc = directory.LastWriteTimeUtc;
        foreach (var file in directory.EnumerateFiles())
        {
            if (file.LastWriteTimeUtc > lastWriteTimeUtc)
            {
                lastWriteTimeUtc = file.LastWriteTimeUtc;
            }
        }

        return lastWriteTimeUtc;
    }

    private string GetBatchDirectory(Guid batchId)
    {
        var path = Path.GetFullPath(Path.Combine(_rootPath, batchId.ToString("N")));
        var rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Temporary reconciliation path is outside the configured root.");
        }

        return path;
    }

    private static string GetOrCreateStorageKey(string rootPath)
    {
        var identityPath = Path.Combine(rootPath, StorageIdentityFileName);
        var temporaryIdentityPath = Path.Combine(
            rootPath,
            $"{StorageIdentityFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(rootPath);
            if (File.Exists(identityPath))
            {
                return ReadStorageKey(identityPath);
            }

            var storageKey = Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(
                temporaryIdentityPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(storageKey);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryIdentityPath, identityPath);
                return storageKey;
            }
            catch (IOException) when (File.Exists(identityPath))
            {
                return ReadStorageKey(identityPath);
            }
        }
        catch (ReconciliationTemporaryFileException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ReconciliationTemporaryFileException(
                "Temporary reconciliation storage identity could not be initialized.",
                exception);
        }
        finally
        {
            try
            {
                File.Delete(temporaryIdentityPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A unique unfinished marker does not affect the active storage identity.
            }
        }
    }

    private static string ReadStorageKey(string identityPath)
    {
        var storageKey = File.ReadAllText(identityPath, Encoding.UTF8).Trim();
        if (!Guid.TryParseExact(storageKey, "N", out _))
        {
            throw new ReconciliationTemporaryFileException(
                "Temporary reconciliation storage identity is invalid.");
        }

        return storageKey;
    }

    private static void TryDeleteProbe(string probePath)
    {
        try
        {
            File.Delete(probePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed readiness probe cleanup is retried by the next probe or operator.
        }
    }
}
