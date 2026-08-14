using System.Net;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class S3ReconciliationObjectClient :
    IReconciliationObjectClient,
    IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly S3ReconciliationRequestFactory _requestFactory;

    public S3ReconciliationObjectClient(IOptions<ReconciliationUploadOptions> options)
    {
        var values = options.Value;
        _requestFactory = new S3ReconciliationRequestFactory(values);
        var clientConfiguration = new AmazonS3Config
        {
            ForcePathStyle = values.S3ForcePathStyle
        };

        if (string.IsNullOrWhiteSpace(values.S3ServiceUrl))
        {
            clientConfiguration.RegionEndpoint = RegionEndpoint.GetBySystemName(values.S3Region);
        }
        else
        {
            clientConfiguration.ServiceURL = values.S3ServiceUrl.TrimEnd('/');
            clientConfiguration.AuthenticationRegion = values.S3Region;
        }

        _client = new AmazonS3Client(clientConfiguration);
    }

    public async Task PutAsync(
        string key,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        await _client.PutObjectAsync(
            _requestFactory.CreatePutObjectRequest(key, content),
            cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(
                _requestFactory.CreateGetObjectRequest(key),
                cancellationToken);
            return new OwnedS3ResponseStream(response);
        }
        catch (AmazonS3Exception exception) when (IsNotFound(exception))
        {
            throw new FileNotFoundException(
                $"Object '{key}' was not found in the configured reconciliation bucket.",
                exception);
        }
    }

    public async Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(
                _requestFactory.CreateGetObjectMetadataRequest(key),
                cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (IsNotFound(exception))
        {
            return false;
        }
    }

    public async Task DeleteAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
        {
            return;
        }

        await _client.DeleteObjectsAsync(
            _requestFactory.CreateDeleteObjectsRequest(keys),
            cancellationToken);
    }

    public async Task<ReconciliationObjectPage> ListAsync(
        string prefix,
        string? continuationToken,
        int maxKeys,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.ListObjectsV2Async(
            _requestFactory.CreateListObjectsRequest(
                prefix,
                continuationToken,
                maxKeys),
            cancellationToken);
        var objects = (response.S3Objects ?? [])
            .Select(item => new ReconciliationObjectInfo(
                item.Key,
                new DateTimeOffset(DateTime.SpecifyKind(
                    item.LastModified ?? DateTime.MaxValue,
                    DateTimeKind.Utc))))
            .ToList();

        return new ReconciliationObjectPage(
            objects,
            response.IsTruncated == true ? response.NextContinuationToken : null);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static bool IsNotFound(AmazonS3Exception exception) =>
        exception.StatusCode == HttpStatusCode.NotFound ||
        string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal);

    private sealed class OwnedS3ResponseStream : Stream
    {
        private readonly GetObjectResponse _response;
        private readonly Stream _innerStream;
        private bool _disposed;

        public OwnedS3ResponseStream(GetObjectResponse response)
        {
            _response = response;
            _innerStream = response.ResponseStream;
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _innerStream.Length;

        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override void Flush() => _innerStream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _innerStream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            _innerStream.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _innerStream.Read(buffer);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _innerStream.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            _innerStream.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            _innerStream.Seek(offset, origin);

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await _innerStream.DisposeAsync();
                _response.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
