using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class S3ReconciliationImmutableAuditArchiveIntegrationTests
{
    private const string RequiredVariable =
        "BANKING_RECONCILIATION_IMMUTABLE_AUDIT_TEST_REQUIRED";
    private const string BucketVariable =
        "BANKING_RECONCILIATION_IMMUTABLE_AUDIT_TEST_BUCKET";
    private const string PrefixVariable =
        "BANKING_RECONCILIATION_IMMUTABLE_AUDIT_TEST_PREFIX";
    private const string ServiceUrlVariable =
        "BANKING_RECONCILIATION_IMMUTABLE_AUDIT_TEST_SERVICE_URL";
    private const string RegionVariable =
        "BANKING_RECONCILIATION_IMMUTABLE_AUDIT_TEST_REGION";
    private const string SigningKeyVariable =
        "BANKING_RECONCILIATION_IMMUTABLE_AUDIT_TEST_SIGNING_KEY";

    [Fact]
    public async Task Archive_WritesComplianceLockedObject_AndRejectsDeleteAndRetentionReduction()
    {
        var bucketName = Environment.GetEnvironmentVariable(BucketVariable);
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            Assert.False(
                IsEnabled(RequiredVariable),
                $"{BucketVariable} must be configured for the required immutable audit profile.");
            return;
        }

        var options = CreateOptions(bucketName);
        using var archive = new S3ReconciliationImmutableAuditArchive(Options.Create(options));
        var auditEvent = CreateAuditEvent();

        var objectKey = await archive.WriteAsync([auditEvent]);

        using var client = new AmazonS3Client(CreateClientConfiguration(options));
        var metadata = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = objectKey
        });
        Assert.Equal(ObjectLockMode.Compliance, metadata.ObjectLockMode);
        Assert.True(metadata.ObjectLockRetainUntilDate > DateTime.UtcNow.AddDays(3649));
        Assert.Equal(
            S3ReconciliationImmutableAuditArchive
                .CreateSignedDocument([auditEvent], options)
                .PayloadHash,
            metadata.Metadata["payload-sha256"]);

        var deleteException = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey
            }));
        Assert.Equal(HttpStatusCode.Forbidden, deleteException.StatusCode);

        var retentionException = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            client.PutObjectRetentionAsync(new PutObjectRetentionRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                BypassGovernanceRetention = true,
                Retention = new ObjectLockRetention
                {
                    Mode = ObjectLockRetentionMode.Compliance,
                    RetainUntilDate = DateTime.UtcNow.AddDays(1)
                }
            }));
        Assert.True(
            retentionException.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,
            $"Unexpected retention rejection status: {retentionException.StatusCode}.");
    }

    private static ReconciliationImmutableAuditArchiveOptions CreateOptions(string bucketName)
    {
        var serviceUrl = Environment.GetEnvironmentVariable(ServiceUrlVariable) ?? string.Empty;
        return new ReconciliationImmutableAuditArchiveOptions
        {
            Enabled = true,
            BucketName = bucketName,
            Prefix = $"{Environment.GetEnvironmentVariable(PrefixVariable) ?? "audit"}/{Guid.NewGuid():N}",
            Region = Environment.GetEnvironmentVariable(RegionVariable) ?? "us-east-1",
            ServiceUrl = serviceUrl,
            ForcePathStyle = !string.IsNullOrWhiteSpace(serviceUrl),
            ObjectLockRetentionDays = 3650,
            SigningKeyId = "ci-hmac-key",
            SigningKeyBase64 = Environment.GetEnvironmentVariable(SigningKeyVariable) ?? string.Empty
        };
    }

    private static AmazonS3Config CreateClientConfiguration(
        ReconciliationImmutableAuditArchiveOptions options)
    {
        var configuration = new AmazonS3Config { ForcePathStyle = options.ForcePathStyle };
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            configuration.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            configuration.ServiceURL = options.ServiceUrl;
            configuration.AuthenticationRegion = options.Region;
        }
        return configuration;
    }

    private static ReconciliationAuditEvent CreateAuditEvent()
    {
        var auditEvent = new ReconciliationAuditEvent
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddYears(-1),
            ArchivedAt = DateTimeOffset.UtcNow,
            Actor = "ci-audit-admin",
            Action = ReconciliationAuditAction.ComparisonSettingsUpdated,
            ResourceType = ReconciliationAuditResourceType.ComparisonSettings,
            ResourceId = "active",
            BeforeStateJson = "{\"normalizeCodeCase\":false}",
            AfterStateJson = "{\"normalizeCodeCase\":true}"
        };
        auditEvent.IntegrityHash = ReconciliationAuditIntegrity.ComputeHash(auditEvent);
        return auditEvent;
    }

    private static bool IsEnabled(string variable) =>
        bool.TryParse(Environment.GetEnvironmentVariable(variable), out var enabled) && enabled;
}
