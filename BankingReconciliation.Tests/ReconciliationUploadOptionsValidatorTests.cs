using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Tests;

public class ReconciliationUploadOptionsValidatorTests
{
    [Fact]
    public void HasValidTemporaryStorage_AcceptsAwsAndMinioConfigurations()
    {
        var awsOptions = new ReconciliationUploadOptions
        {
            TemporaryStorageMode = ReconciliationTemporaryStorageMode.S3Compatible,
            S3BucketName = "reconciliation-production",
            S3Prefix = "banking-reconciliation/uploads",
            S3Region = "eu-central-1"
        };
        var minioOptions = new ReconciliationUploadOptions
        {
            TemporaryStorageMode = ReconciliationTemporaryStorageMode.S3Compatible,
            S3BucketName = "reconciliation-production",
            S3Prefix = "banking-reconciliation/uploads",
            S3Region = "us-east-1",
            S3ServiceUrl = "https://minio.internal.example",
            S3ForcePathStyle = true
        };

        Assert.True(ReconciliationUploadOptionsValidator.HasValidTemporaryStorage(awsOptions));
        Assert.True(ReconciliationUploadOptionsValidator.HasValidTemporaryStorage(minioOptions));
    }

    [Theory]
    [InlineData(ReconciliationS3ServerSideEncryption.BucketDefault, "", "")]
    [InlineData(ReconciliationS3ServerSideEncryption.AES256, "", "")]
    [InlineData(
        ReconciliationS3ServerSideEncryption.AwsKms,
        "arn:aws:kms:eu-central-1:123456789012:key/example",
        "123456789012")]
    public void HasValidTemporaryStorage_AcceptsValidEncryptionAndOwnerConfiguration(
        ReconciliationS3ServerSideEncryption encryption,
        string kmsKeyId,
        string expectedBucketOwner)
    {
        var options = CreateValidS3Options();
        options.S3ServerSideEncryption = encryption;
        options.S3KmsKeyId = kmsKeyId;
        options.S3ExpectedBucketOwner = expectedBucketOwner;

        Assert.True(ReconciliationUploadOptionsValidator.HasValidTemporaryStorage(options));
    }

    [Theory]
    [InlineData(ReconciliationS3ServerSideEncryption.AwsKms, "", "")]
    [InlineData(ReconciliationS3ServerSideEncryption.BucketDefault, "unexpected-key", "")]
    [InlineData(ReconciliationS3ServerSideEncryption.AES256, "unexpected-key", "")]
    [InlineData(ReconciliationS3ServerSideEncryption.BucketDefault, "", "123")]
    [InlineData(ReconciliationS3ServerSideEncryption.BucketDefault, "", "12345678901x")]
    [InlineData((ReconciliationS3ServerSideEncryption)999, "", "")]
    public void HasValidTemporaryStorage_RejectsInvalidEncryptionOrOwnerConfiguration(
        ReconciliationS3ServerSideEncryption encryption,
        string kmsKeyId,
        string expectedBucketOwner)
    {
        var options = CreateValidS3Options();
        options.S3ServerSideEncryption = encryption;
        options.S3KmsKeyId = kmsKeyId;
        options.S3ExpectedBucketOwner = expectedBucketOwner;

        Assert.False(ReconciliationUploadOptionsValidator.HasValidTemporaryStorage(options));
    }

    [Theory]
    [InlineData("", "banking-reconciliation/uploads", "us-east-1", "")]
    [InlineData("bucket", "", "us-east-1", "")]
    [InlineData("bucket", "uploads", "", "")]
    [InlineData("bucket", "uploads", "us-east-1", "ftp://object-store")]
    [InlineData("bucket", "uploads", "us-east-1", "https://user:password@object-store")]
    public void HasValidTemporaryStorage_RejectsIncompleteOrUnsafeS3Configuration(
        string bucketName,
        string prefix,
        string region,
        string serviceUrl)
    {
        var options = new ReconciliationUploadOptions
        {
            TemporaryStorageMode = ReconciliationTemporaryStorageMode.S3Compatible,
            S3BucketName = bucketName,
            S3Prefix = prefix,
            S3Region = region,
            S3ServiceUrl = serviceUrl
        };

        Assert.False(ReconciliationUploadOptionsValidator.HasValidTemporaryStorage(options));
    }

    private static ReconciliationUploadOptions CreateValidS3Options() =>
        new()
        {
            TemporaryStorageMode = ReconciliationTemporaryStorageMode.S3Compatible,
            S3BucketName = "reconciliation-production",
            S3Prefix = "banking-reconciliation/uploads",
            S3Region = "eu-central-1"
        };
}
