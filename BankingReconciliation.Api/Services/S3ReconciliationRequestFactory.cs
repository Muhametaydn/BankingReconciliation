using Amazon.S3;
using Amazon.S3.Model;
using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Api.Services;

internal sealed class S3ReconciliationRequestFactory
{
    private readonly string _bucketName;
    private readonly string? _expectedBucketOwner;
    private readonly ReconciliationS3ServerSideEncryption _serverSideEncryption;
    private readonly string? _kmsKeyId;

    public S3ReconciliationRequestFactory(ReconciliationUploadOptions options)
    {
        _bucketName = options.S3BucketName;
        _expectedBucketOwner = NullIfWhiteSpace(options.S3ExpectedBucketOwner);
        _serverSideEncryption = options.S3ServerSideEncryption;
        _kmsKeyId = NullIfWhiteSpace(options.S3KmsKeyId);
    }

    public PutObjectRequest CreatePutObjectRequest(string key, Stream content)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ExpectedBucketOwner = _expectedBucketOwner
        };

        if (_serverSideEncryption == ReconciliationS3ServerSideEncryption.AES256)
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        }
        else if (_serverSideEncryption == ReconciliationS3ServerSideEncryption.AwsKms)
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS;
            request.ServerSideEncryptionKeyManagementServiceKeyId = _kmsKeyId;
        }

        return request;
    }

    public GetObjectRequest CreateGetObjectRequest(string key) =>
        new()
        {
            BucketName = _bucketName,
            Key = key,
            ExpectedBucketOwner = _expectedBucketOwner
        };

    public GetObjectMetadataRequest CreateGetObjectMetadataRequest(string key) =>
        new()
        {
            BucketName = _bucketName,
            Key = key,
            ExpectedBucketOwner = _expectedBucketOwner
        };

    public DeleteObjectsRequest CreateDeleteObjectsRequest(
        IReadOnlyCollection<string> keys) =>
        new()
        {
            BucketName = _bucketName,
            ExpectedBucketOwner = _expectedBucketOwner,
            Quiet = true,
            Objects = keys
                .Select(key => new KeyVersion { Key = key })
                .ToList()
        };

    public ListObjectsV2Request CreateListObjectsRequest(
        string prefix,
        string? continuationToken,
        int maxKeys) =>
        new()
        {
            BucketName = _bucketName,
            Prefix = prefix,
            ContinuationToken = continuationToken,
            MaxKeys = maxKeys,
            ExpectedBucketOwner = _expectedBucketOwner
        };

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
