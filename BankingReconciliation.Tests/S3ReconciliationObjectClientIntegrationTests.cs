using System.Text;
using System.Net;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class S3ReconciliationObjectClientIntegrationTests
{
    private const string BucketEnvironmentVariable =
        "BANKING_RECONCILIATION_S3_TEST_BUCKET";
    private const string ServiceUrlEnvironmentVariable =
        "BANKING_RECONCILIATION_S3_TEST_SERVICE_URL";
    private const string RegionEnvironmentVariable =
        "BANKING_RECONCILIATION_S3_TEST_REGION";
    private const string PrefixEnvironmentVariable =
        "BANKING_RECONCILIATION_S3_TEST_PREFIX";
    private const string RequiredEnvironmentVariable =
        "BANKING_RECONCILIATION_S3_TEST_REQUIRED";
    private const string EnforceLeastPrivilegeEnvironmentVariable =
        "BANKING_RECONCILIATION_S3_TEST_ENFORCE_LEAST_PRIVILEGE";

    [Fact]
    public async Task Store_RoundTripsThroughS3CompatibleStorage_WhenConfigured()
    {
        var bucketName = Environment.GetEnvironmentVariable(BucketEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            Assert.False(
                IsEnabled(RequiredEnvironmentVariable),
                $"{BucketEnvironmentVariable} must be configured for the required S3 integration profile.");
            return;
        }

        var options = CreateOptions(bucketName);
        using var objectClient = new S3ReconciliationObjectClient(Options.Create(options));
        var store = new S3ReconciliationTemporaryFileStore(
            objectClient,
            Options.Create(options));
        var batchId = Guid.NewGuid();

        try
        {
            await store.SaveBranchStreamAsync(
                batchId,
                new MemoryStream(Encoding.UTF8.GetBytes("branch-s3")));
            await store.SaveBankStreamAsync(
                batchId,
                new MemoryStream(Encoding.UTF8.GetBytes("bank-s3")));

            Assert.True(await store.ExistsAsync(batchId));
            await store.VerifyAvailabilityAsync();
            await using var stream = await store.OpenBankReadAsync(batchId);
            using var reader = new StreamReader(stream);
            Assert.Equal("bank-s3", await reader.ReadToEndAsync());
        }
        finally
        {
            await store.DeleteAsync(batchId, CancellationToken.None);
        }

        Assert.False(await store.ExistsAsync(batchId));
    }

    [Fact]
    public async Task ApplicationIdentity_CannotEscapePrefixOrReadLifecycle_WhenEnforced()
    {
        if (!IsEnabled(EnforceLeastPrivilegeEnvironmentVariable))
        {
            return;
        }

        var bucketName = Environment.GetEnvironmentVariable(BucketEnvironmentVariable);
        Assert.False(
            string.IsNullOrWhiteSpace(bucketName),
            $"{BucketEnvironmentVariable} must be configured when least-privilege checks are enabled.");

        var options = CreateOptions(bucketName!);
        using var client = CreateAmazonS3Client(options);
        var deniedKey = $"outside-reconciliation-prefix/{Guid.NewGuid():N}.dat";

        var putException = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = deniedKey,
                ContentBody = "must-not-be-written"
            }));
        Assert.Equal(HttpStatusCode.Forbidden, putException.StatusCode);

        var lifecycleException = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            client.GetLifecycleConfigurationAsync(new GetLifecycleConfigurationRequest
            {
                BucketName = bucketName
            }));
        Assert.Equal(HttpStatusCode.Forbidden, lifecycleException.StatusCode);
    }

    private static ReconciliationUploadOptions CreateOptions(string bucketName)
    {
        var serviceUrl = Environment.GetEnvironmentVariable(ServiceUrlEnvironmentVariable) ??
            string.Empty;
        var configuredPrefix = Environment.GetEnvironmentVariable(PrefixEnvironmentVariable);
        var rootPrefix = string.IsNullOrWhiteSpace(configuredPrefix)
            ? "banking-reconciliation-integration"
            : configuredPrefix.Trim().Trim('/');

        return new ReconciliationUploadOptions
        {
            TemporaryStorageMode = ReconciliationTemporaryStorageMode.S3Compatible,
            MaxCsvFileSizeBytes = 1024,
            S3BucketName = bucketName,
            S3Prefix = $"{rootPrefix}/{Guid.NewGuid():N}",
            S3Region = Environment.GetEnvironmentVariable(RegionEnvironmentVariable) ??
                "us-east-1",
            S3ServiceUrl = serviceUrl,
            S3ForcePathStyle = !string.IsNullOrWhiteSpace(serviceUrl)
        };
    }

    private static AmazonS3Client CreateAmazonS3Client(ReconciliationUploadOptions options)
    {
        var configuration = new AmazonS3Config
        {
            ForcePathStyle = options.S3ForcePathStyle
        };
        if (string.IsNullOrWhiteSpace(options.S3ServiceUrl))
        {
            configuration.RegionEndpoint = RegionEndpoint.GetBySystemName(options.S3Region);
        }
        else
        {
            configuration.ServiceURL = options.S3ServiceUrl.TrimEnd('/');
            configuration.AuthenticationRegion = options.S3Region;
        }

        return new AmazonS3Client(configuration);
    }

    private static bool IsEnabled(string environmentVariable) =>
        bool.TryParse(
            Environment.GetEnvironmentVariable(environmentVariable),
            out var enabled) && enabled;
}
