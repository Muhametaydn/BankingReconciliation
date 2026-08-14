using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class S3ReconciliationImmutableAuditArchive :
    IReconciliationImmutableAuditArchive,
    IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ReconciliationImmutableAuditArchiveOptions _options;
    private readonly AmazonS3Client _client;
    private readonly IReconciliationAuditArchiveSigner _signer;

    public S3ReconciliationImmutableAuditArchive(
        IOptions<ReconciliationImmutableAuditArchiveOptions> options)
        : this(options, new LocalReconciliationAuditArchiveSigner(options))
    {
    }

    internal S3ReconciliationImmutableAuditArchive(
        IOptions<ReconciliationImmutableAuditArchiveOptions> options,
        IReconciliationAuditArchiveSigner signer)
    {
        _options = options.Value;
        _signer = signer;
        _client = new AmazonS3Client(CreateClientConfiguration(_options));
    }

    public bool Enabled => true;

    public async Task<string> WriteAsync(
        IReadOnlyCollection<ReconciliationAuditEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(events.Count);
        var document = await CreateSignedDocumentAsync(
            events,
            _options,
            _signer,
            cancellationToken);

        try
        {
            var existing = await _client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = _options.BucketName,
                    Key = document.ObjectKey,
                    ExpectedBucketOwner = NullIfWhiteSpace(_options.ExpectedBucketOwner)
                },
                cancellationToken);
            ValidateExistingObject(existing, document);
            return document.ObjectKey;
        }
        catch (AmazonS3Exception exception) when (IsNotFound(exception))
        {
            // The deterministic object does not exist yet and can be created below.
        }

        await using var content = new MemoryStream(document.Content, writable: false);
        var request = CreatePutObjectRequest(document, content, _options);
        await _client.PutObjectAsync(request, cancellationToken);

        var metadata = await _client.GetObjectMetadataAsync(
            new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = document.ObjectKey,
                ExpectedBucketOwner = NullIfWhiteSpace(_options.ExpectedBucketOwner)
            },
            cancellationToken);
        ValidateExistingObject(metadata, document);
        return document.ObjectKey;
    }

    internal static SignedAuditArchiveDocument CreateSignedDocument(
        IReadOnlyCollection<ReconciliationAuditEvent> events,
        ReconciliationImmutableAuditArchiveOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfZero(events.Count);
        var records = events
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => new AuditArchiveRecord(
                item.Id,
                item.CreatedAt,
                item.Actor,
                item.Action.ToString(),
                item.ResourceType.ToString(),
                item.ResourceId,
                item.BeforeStateJson,
                item.AfterStateJson,
                item.ArchivedAt,
                item.IntegrityHash))
            .ToArray();
        var archiveCreatedAt = records
            .Select(item => item.ArchivedAt ?? item.CreatedAt)
            .Max();
        var payload = new AuditArchivePayload(
            SchemaVersion: 1,
            ArchiveCreatedAt: archiveCreatedAt,
            SigningKeyId: options.SigningKeyId,
            Records: records);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        var payloadHash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
        var signature = ReconciliationAuditArchiveSigner.Sign(payloadBytes, options);
        var envelope = new AuditArchiveEnvelope(
            payload,
            payloadHash,
            signature.Algorithm,
            signature.Value);
        var content = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        var firstDate = records[0].CreatedAt.UtcDateTime;
        var objectKey = $"{options.Prefix.Trim('/')}/{firstDate:yyyy/MM/dd}/{payloadHash}.json";

        return new SignedAuditArchiveDocument(
            objectKey,
            content,
            payloadHash,
            signature.Algorithm,
            signature.Value);
    }

    internal static async Task<SignedAuditArchiveDocument> CreateSignedDocumentAsync(
        IReadOnlyCollection<ReconciliationAuditEvent> events,
        ReconciliationImmutableAuditArchiveOptions options,
        IReconciliationAuditArchiveSigner signer,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(events.Count);
        var records = events
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => new AuditArchiveRecord(
                item.Id,
                item.CreatedAt,
                item.Actor,
                item.Action.ToString(),
                item.ResourceType.ToString(),
                item.ResourceId,
                item.BeforeStateJson,
                item.AfterStateJson,
                item.ArchivedAt,
                item.IntegrityHash))
            .ToArray();
        var archiveCreatedAt = records
            .Select(item => item.ArchivedAt ?? item.CreatedAt)
            .Max();
        var payload = new AuditArchivePayload(
            SchemaVersion: 1,
            ArchiveCreatedAt: archiveCreatedAt,
            SigningKeyId: options.SigningKeyId,
            Records: records);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        var payloadHash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
        var signature = await signer.SignAsync(payloadBytes, cancellationToken);
        var envelope = new AuditArchiveEnvelope(
            payload,
            payloadHash,
            signature.Algorithm,
            signature.Value);
        var content = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        var firstDate = records[0].CreatedAt.UtcDateTime;
        var objectKey = $"{options.Prefix.Trim('/')}/{firstDate:yyyy/MM/dd}/{payloadHash}.json";

        return new SignedAuditArchiveDocument(
            objectKey,
            content,
            payloadHash,
            signature.Algorithm,
            signature.Value);
    }

    internal static PutObjectRequest CreatePutObjectRequest(
        SignedAuditArchiveDocument document,
        Stream content,
        ReconciliationImmutableAuditArchiveOptions options)
    {
        var request = new PutObjectRequest
        {
            BucketName = options.BucketName,
            Key = document.ObjectKey,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = "application/json",
            ExpectedBucketOwner = NullIfWhiteSpace(options.ExpectedBucketOwner),
            ChecksumSHA256 = Convert.ToBase64String(SHA256.HashData(document.Content)),
            ObjectLockMode = ObjectLockMode.Compliance,
            ObjectLockRetainUntilDate = DateTime.UtcNow.AddDays(options.ObjectLockRetentionDays)
        };
        request.Metadata["payload-sha256"] = document.PayloadHash;
        request.Metadata["signature-algorithm"] = document.SignatureAlgorithm;
        request.Metadata["signature"] = document.Signature;
        request.Metadata["signing-key-id"] = options.SigningKeyId;
        return request;
    }

    public void Dispose() => _client.Dispose();

    private static AmazonS3Config CreateClientConfiguration(
        ReconciliationImmutableAuditArchiveOptions options)
    {
        var configuration = new AmazonS3Config { ForcePathStyle = options.ForcePathStyle };
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            configuration.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            configuration.ServiceURL = options.ServiceUrl.TrimEnd('/');
            configuration.AuthenticationRegion = options.Region;
        }
        return configuration;
    }

    private static void ValidateExistingObject(
        GetObjectMetadataResponse metadata,
        SignedAuditArchiveDocument document)
    {
        if (metadata.ObjectLockMode != ObjectLockMode.Compliance ||
            metadata.ObjectLockRetainUntilDate is null ||
            metadata.ObjectLockRetainUntilDate <= DateTime.UtcNow ||
            !string.Equals(
                metadata.Metadata["payload-sha256"],
                document.PayloadHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The immutable audit archive object is missing the expected COMPLIANCE lock or payload hash.");
        }
    }

    private static bool IsNotFound(AmazonS3Exception exception) =>
        exception.StatusCode == HttpStatusCode.NotFound ||
        string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal);

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal sealed record SignedAuditArchiveDocument(
        string ObjectKey,
        byte[] Content,
        string PayloadHash,
        string SignatureAlgorithm,
        string Signature);

    private sealed record AuditArchivePayload(
        int SchemaVersion,
        DateTimeOffset ArchiveCreatedAt,
        string SigningKeyId,
        IReadOnlyCollection<AuditArchiveRecord> Records);

    private sealed record AuditArchiveRecord(
        Guid Id,
        DateTimeOffset CreatedAt,
        string Actor,
        string Action,
        string ResourceType,
        string ResourceId,
        string? BeforeStateJson,
        string? AfterStateJson,
        DateTimeOffset? ArchivedAt,
        string? IntegrityHash);

    private sealed record AuditArchiveEnvelope(
        AuditArchivePayload Payload,
        string PayloadHash,
        string SignatureAlgorithm,
        string Signature);
}
