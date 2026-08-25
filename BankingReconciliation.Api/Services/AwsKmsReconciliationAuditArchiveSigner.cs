using System.Security.Cryptography;
using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

internal sealed class AwsKmsReconciliationAuditArchiveSigner :
    IReconciliationAuditArchiveSigner,
    IDisposable
{
    private const string AlgorithmName = "AWS-KMS-RSA-PSS-SHA256";
    private readonly ReconciliationImmutableAuditArchiveOptions _options;
    private readonly IAmazonKeyManagementService _client;
    private readonly bool _ownsClient;

    public AwsKmsReconciliationAuditArchiveSigner(
        IOptions<ReconciliationImmutableAuditArchiveOptions> options)
        : this(
            options,
            new AmazonKeyManagementServiceClient(new AmazonKeyManagementServiceConfig
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(options.Value.KmsRegion)
            }),
            ownsClient: true)
    {
    }

    internal AwsKmsReconciliationAuditArchiveSigner(
        IOptions<ReconciliationImmutableAuditArchiveOptions> options,
        IAmazonKeyManagementService client,
        bool ownsClient = false)
    {
        _options = options.Value;
        _client = client;
        _ownsClient = ownsClient;
    }

    public async Task<AuditArchiveSignature> SignAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        var digest = SHA256.HashData(payload.Span);
        var response = await _client.SignAsync(
            CreateSignRequest(_options.KmsKeyId, digest),
            cancellationToken);
        var signatureBytes = response.Signature.ToArray();
        var verification = await _client.VerifyAsync(
            CreateVerifyRequest(_options.KmsKeyId, digest, signatureBytes),
            cancellationToken);
        if (verification.SignatureValid != true)
        {
            throw new CryptographicException("AWS KMS did not verify the audit archive signature.");
        }

        return new AuditArchiveSignature(
            AlgorithmName,
            Convert.ToBase64String(signatureBytes));
    }

    internal static SignRequest CreateSignRequest(string keyId, byte[] digest) => new()
    {
        KeyId = keyId,
        Message = new MemoryStream(digest, writable: false),
        MessageType = MessageType.DIGEST,
        SigningAlgorithm = SigningAlgorithmSpec.RSASSA_PSS_SHA_256
    };

    internal static VerifyRequest CreateVerifyRequest(
        string keyId,
        byte[] digest,
        byte[] signature) => new()
        {
            KeyId = keyId,
            Message = new MemoryStream(digest, writable: false),
            MessageType = MessageType.DIGEST,
            Signature = new MemoryStream(signature, writable: false),
            SigningAlgorithm = SigningAlgorithmSpec.RSASSA_PSS_SHA_256
        };

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
