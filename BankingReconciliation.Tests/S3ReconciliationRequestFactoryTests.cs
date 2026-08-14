using Amazon.S3;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;

namespace BankingReconciliation.Tests;

public class S3ReconciliationRequestFactoryTests
{
    [Fact]
    public void CreateRequests_AppliesExpectedOwnerToEveryBucketRequest()
    {
        var factory = CreateFactory(
            ReconciliationS3ServerSideEncryption.BucketDefault,
            expectedBucketOwner: " 123456789012 ");
        using var content = new MemoryStream([1, 2, 3]);

        var put = factory.CreatePutObjectRequest("prefix/file.dat", content);
        var get = factory.CreateGetObjectRequest("prefix/file.dat");
        var metadata = factory.CreateGetObjectMetadataRequest("prefix/file.dat");
        var delete = factory.CreateDeleteObjectsRequest(
            ["prefix/first.dat", "prefix/second.dat"]);
        var list = factory.CreateListObjectsRequest("prefix/", "next-token", 25);

        Assert.Equal("123456789012", put.ExpectedBucketOwner);
        Assert.Equal("123456789012", get.ExpectedBucketOwner);
        Assert.Equal("123456789012", metadata.ExpectedBucketOwner);
        Assert.Equal("123456789012", delete.ExpectedBucketOwner);
        Assert.Equal("123456789012", list.ExpectedBucketOwner);
        Assert.Null(put.ServerSideEncryptionMethod);
        Assert.Null(put.ServerSideEncryptionKeyManagementServiceKeyId);
        Assert.True(delete.Quiet);
        Assert.Equal(
            ["prefix/first.dat", "prefix/second.dat"],
            delete.Objects.Select(item => item.Key));
        Assert.Equal("next-token", list.ContinuationToken);
        Assert.Equal(25, list.MaxKeys);
    }

    [Fact]
    public void CreatePutObjectRequest_ConfiguresAes256Encryption()
    {
        var factory = CreateFactory(ReconciliationS3ServerSideEncryption.AES256);
        using var content = new MemoryStream([1]);

        var request = factory.CreatePutObjectRequest("prefix/file.dat", content);

        Assert.Equal(ServerSideEncryptionMethod.AES256, request.ServerSideEncryptionMethod);
        Assert.Null(request.ServerSideEncryptionKeyManagementServiceKeyId);
    }

    [Fact]
    public void CreatePutObjectRequest_ConfiguresAwsKmsEncryptionAndKey()
    {
        var factory = CreateFactory(
            ReconciliationS3ServerSideEncryption.AwsKms,
            kmsKeyId: "arn:aws:kms:eu-central-1:123456789012:key/example");
        using var content = new MemoryStream([1]);

        var request = factory.CreatePutObjectRequest("prefix/file.dat", content);

        Assert.Equal(ServerSideEncryptionMethod.AWSKMS, request.ServerSideEncryptionMethod);
        Assert.Equal(
            "arn:aws:kms:eu-central-1:123456789012:key/example",
            request.ServerSideEncryptionKeyManagementServiceKeyId);
    }

    private static S3ReconciliationRequestFactory CreateFactory(
        ReconciliationS3ServerSideEncryption encryption,
        string kmsKeyId = "",
        string expectedBucketOwner = "") =>
        new(
            new ReconciliationUploadOptions
            {
                S3BucketName = "reconciliation-test",
                S3ServerSideEncryption = encryption,
                S3KmsKeyId = kmsKeyId,
                S3ExpectedBucketOwner = expectedBucketOwner
            });
}
