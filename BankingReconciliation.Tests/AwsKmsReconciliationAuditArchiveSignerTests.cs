using System.Security.Cryptography;
using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class AwsKmsReconciliationAuditArchiveSignerTests
{
    [Fact]
    public async Task SignAsync_SendsDigestWithRsaPss_AndRequiresKmsVerification()
    {
        var options = CreateOptions();
        using var client = new TestKmsClient();
        using var signer = new AwsKmsReconciliationAuditArchiveSigner(
            Options.Create(options),
            client);
        var payload = "audit-payload-larger-than-a-digest"u8.ToArray();

        var signature = await signer.SignAsync(payload);

        Assert.Equal("AWS-KMS-RSA-PSS-SHA256", signature.Algorithm);
        Assert.NotEmpty(Convert.FromBase64String(signature.Value));
        Assert.NotNull(client.SignRequest);
        Assert.NotNull(client.VerifyRequest);
        Assert.Equal("alias/reconciliation-audit", client.SignRequest.KeyId);
        Assert.Equal(MessageType.DIGEST, client.SignRequest.MessageType);
        Assert.Equal(
            SigningAlgorithmSpec.RSASSA_PSS_SHA_256,
            client.SignRequest.SigningAlgorithm);
        Assert.Equal(SHA256.HashData(payload), client.SignRequest.Message.ToArray());
        Assert.Equal(client.SignRequest.Message.ToArray(), client.VerifyRequest.Message.ToArray());
    }

    [Fact]
    public void OptionsValidator_AcceptsKmsWithoutLocalPrivateKey_AndRejectsMissingKeyId()
    {
        var options = CreateOptions();
        Assert.True(ReconciliationImmutableAuditArchiveOptionsValidator.IsValid(options));

        options.KmsKeyId = string.Empty;
        Assert.False(ReconciliationImmutableAuditArchiveOptionsValidator.IsValid(options));
    }

    private static ReconciliationImmutableAuditArchiveOptions CreateOptions() => new()
    {
        Enabled = true,
        BucketName = "audit-bucket",
        Prefix = "audit",
        Region = "eu-central-1",
        ObjectLockRetentionDays = 3650,
        SigningAlgorithm = ReconciliationAuditSigningAlgorithm.AwsKmsRsaPssSha256,
        SigningKeyId = "audit-kms-2026",
        KmsKeyId = "alias/reconciliation-audit",
        KmsRegion = "eu-central-1"
    };

    private sealed class TestKmsClient : AmazonKeyManagementServiceClient
    {
        private readonly RSA _rsa = RSA.Create(2048);

        public TestKmsClient()
            : base(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
        {
        }

        public SignRequest? SignRequest { get; private set; }
        public VerifyRequest? VerifyRequest { get; private set; }

        public override Task<SignResponse> SignAsync(
            SignRequest request,
            CancellationToken cancellationToken = default)
        {
            SignRequest = request;
            var signature = _rsa.SignHash(
                request.Message.ToArray(),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
            return Task.FromResult(new SignResponse
            {
                KeyId = request.KeyId,
                SigningAlgorithm = request.SigningAlgorithm,
                Signature = new MemoryStream(signature, writable: false)
            });
        }

        public override Task<VerifyResponse> VerifyAsync(
            VerifyRequest request,
            CancellationToken cancellationToken = default)
        {
            VerifyRequest = request;
            var valid = _rsa.VerifyHash(
                request.Message.ToArray(),
                request.Signature.ToArray(),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
            return Task.FromResult(new VerifyResponse
            {
                KeyId = request.KeyId,
                SigningAlgorithm = request.SigningAlgorithm,
                SignatureValid = valid
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _rsa.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
