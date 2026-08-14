using System.Net;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class AwsKmsS3ImmutableAuditArchiveIntegrationTests
{
    private const string RequiredVariable =
        "BANKING_RECONCILIATION_AWS_WORM_TEST_REQUIRED";
    private const string BucketVariable =
        "BANKING_RECONCILIATION_AWS_WORM_TEST_BUCKET";
    private const string PrefixVariable =
        "BANKING_RECONCILIATION_AWS_WORM_TEST_PREFIX";
    private const string RegionVariable =
        "BANKING_RECONCILIATION_AWS_WORM_TEST_REGION";
    private const string OwnerVariable =
        "BANKING_RECONCILIATION_AWS_WORM_TEST_EXPECTED_OWNER";
    private const string KmsKeyVariable =
        "BANKING_RECONCILIATION_AWS_WORM_TEST_KMS_KEY_ID";
    private const string RetentionDaysVariable =
        "BANKING_RECONCILIATION_AWS_WORM_TEST_RETENTION_DAYS";

    [Fact]
    public async Task Archive_UsesRealKmsAndComplianceLockedS3_WhenConfigured()
    {
        var bucketName = Environment.GetEnvironmentVariable(BucketVariable);
        var kmsKeyId = Environment.GetEnvironmentVariable(KmsKeyVariable);
        if (string.IsNullOrWhiteSpace(bucketName) || string.IsNullOrWhiteSpace(kmsKeyId))
        {
            Assert.False(
                IsEnabled(RequiredVariable),
                $"{BucketVariable} and {KmsKeyVariable} must be configured for the required AWS WORM profile.");
            return;
        }

        var options = CreateOptions(bucketName, kmsKeyId);
        Assert.True(ReconciliationImmutableAuditArchiveOptionsValidator.IsValid(options));
        var configuredOptions = Options.Create(options);
        using var signer = new AwsKmsReconciliationAuditArchiveSigner(configuredOptions);
        using var archive = new S3ReconciliationImmutableAuditArchive(
            configuredOptions,
            signer);
        var auditEvent = CreateAuditEvent();

        var objectKey = await archive.WriteAsync([auditEvent]);

        using var s3 = new AmazonS3Client(new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region)
        });
        var metadata = await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            ExpectedBucketOwner = EmptyToNull(options.ExpectedBucketOwner)
        });
        Assert.Equal(ObjectLockMode.Compliance, metadata.ObjectLockMode);
        Assert.True(
            metadata.ObjectLockRetainUntilDate >
                DateTime.UtcNow.AddDays(options.ObjectLockRetentionDays - 1));
        Assert.Equal("AWS-KMS-RSA-PSS-SHA256", metadata.Metadata["signature-algorithm"]);
        Assert.Equal(options.SigningKeyId, metadata.Metadata["signing-key-id"]);
        Assert.Matches("^[a-f0-9]{64}$", metadata.Metadata["payload-sha256"]);

        var deleteException = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                ExpectedBucketOwner = EmptyToNull(options.ExpectedBucketOwner)
            }));
        Assert.Equal(HttpStatusCode.Forbidden, deleteException.StatusCode);
    }

    private static ReconciliationImmutableAuditArchiveOptions CreateOptions(
        string bucketName,
        string kmsKeyId)
    {
        var retentionDays = int.TryParse(
            Environment.GetEnvironmentVariable(RetentionDaysVariable),
            out var configuredRetentionDays)
            ? configuredRetentionDays
            : 1;
        var configuredRegion = Environment.GetEnvironmentVariable(RegionVariable);
        var region = string.IsNullOrWhiteSpace(configuredRegion)
            ? "eu-central-1"
            : configuredRegion;
        return new ReconciliationImmutableAuditArchiveOptions
        {
            Enabled = true,
            BucketName = bucketName,
            Prefix = $"{Environment.GetEnvironmentVariable(PrefixVariable) ?? "banking-reconciliation/audit-integration"}/{Guid.NewGuid():N}",
            Region = region,
            ExpectedBucketOwner = Environment.GetEnvironmentVariable(OwnerVariable) ?? string.Empty,
            ObjectLockRetentionDays = retentionDays,
            SigningAlgorithm = ReconciliationAuditSigningAlgorithm.AwsKmsRsaPssSha256,
            SigningKeyId = kmsKeyId,
            KmsKeyId = kmsKeyId,
            KmsRegion = region
        };
    }

    private static ReconciliationAuditEvent CreateAuditEvent()
    {
        var auditEvent = new ReconciliationAuditEvent
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddYears(-1),
            ArchivedAt = DateTimeOffset.UtcNow,
            Actor = "aws-kms-integration",
            Action = ReconciliationAuditAction.FileSchemaUpdated,
            ResourceType = ReconciliationAuditResourceType.FileSchema,
            ResourceId = "active",
            BeforeStateJson = "{\"version\":1}",
            AfterStateJson = "{\"version\":2}"
        };
        auditEvent.IntegrityHash = ReconciliationAuditIntegrity.ComputeHash(auditEvent);
        return auditEvent;
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool IsEnabled(string variable) =>
        bool.TryParse(Environment.GetEnvironmentVariable(variable), out var enabled) && enabled;
}
