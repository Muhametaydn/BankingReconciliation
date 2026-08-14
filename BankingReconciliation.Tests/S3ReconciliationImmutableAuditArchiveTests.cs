using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;

namespace BankingReconciliation.Tests;

public class S3ReconciliationImmutableAuditArchiveTests
{
    [Fact]
    public void CreateSignedDocument_IsDeterministicAndHasVerifiableSignature()
    {
        var options = CreateOptions();
        var events = new[] { CreateAuditEvent() };

        var first = S3ReconciliationImmutableAuditArchive.CreateSignedDocument(events, options);
        var second = S3ReconciliationImmutableAuditArchive.CreateSignedDocument(events, options);

        Assert.Equal(first.ObjectKey, second.ObjectKey);
        Assert.Equal(first.Content, second.Content);
        using var json = JsonDocument.Parse(first.Content);
        var payloadBytes = Encoding.UTF8.GetBytes(
            json.RootElement.GetProperty("payload").GetRawText());
        using var hmac = new HMACSHA256(Convert.FromBase64String(options.SigningKeyBase64));
        var expectedSignature = Convert.ToBase64String(hmac.ComputeHash(payloadBytes));
        Assert.Equal(expectedSignature, json.RootElement.GetProperty("signature").GetString());
        Assert.Equal(
            first.PayloadHash,
            json.RootElement.GetProperty("payloadHash").GetString());
    }

    [Fact]
    public void CreatePutObjectRequest_RequiresComplianceLockAndChecksum()
    {
        var options = CreateOptions();
        var document = S3ReconciliationImmutableAuditArchive.CreateSignedDocument(
            [CreateAuditEvent()],
            options);
        using var content = new MemoryStream(document.Content);

        var request = S3ReconciliationImmutableAuditArchive.CreatePutObjectRequest(
            document,
            content,
            options);

        Assert.Equal(ObjectLockMode.Compliance, request.ObjectLockMode);
        Assert.True(request.ObjectLockRetainUntilDate > DateTime.UtcNow.AddDays(3649));
        Assert.False(string.IsNullOrWhiteSpace(request.ChecksumSHA256));
        Assert.Equal(document.PayloadHash, request.Metadata["payload-sha256"]);
        Assert.Equal("audit-key-2026", request.Metadata["signing-key-id"]);
    }

    [Fact]
    public void OptionsValidator_RequiresStrongKeyAndCredentialFreeServiceUrl()
    {
        Assert.True(ReconciliationImmutableAuditArchiveOptionsValidator.IsValid(CreateOptions()));

        var weakKey = CreateOptions();
        weakKey.SigningKeyBase64 = Convert.ToBase64String(new byte[16]);
        Assert.False(ReconciliationImmutableAuditArchiveOptionsValidator.IsValid(weakKey));

        var credentialUrl = CreateOptions();
        credentialUrl.ServiceUrl = "https://user:password@minio.example";
        Assert.False(ReconciliationImmutableAuditArchiveOptionsValidator.IsValid(credentialUrl));

        var shortObjectLock = CreateOptions();
        shortObjectLock.ObjectLockRetentionDays = 100;
        Assert.False(ReconciliationImmutableAuditArchiveOptionsValidator.IsRetentionCompatible(
            shortObjectLock,
            new ReconciliationAuditRetentionOptions { ArchiveRetentionDays = 2555 }));
    }

    [Fact]
    public void RsaPssSignature_IsIndependentlyVerifiable_AndRejectsChangedPayload()
    {
        using var rsa = RSA.Create(2048);
        var options = CreateOptions();
        options.SigningAlgorithm = ReconciliationAuditSigningAlgorithm.RsaPssSha256;
        options.SigningKeyBase64 = string.Empty;
        options.SigningPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
        options.SigningPublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        Assert.True(ReconciliationImmutableAuditArchiveOptionsValidator.IsValid(options));

        var document = S3ReconciliationImmutableAuditArchive.CreateSignedDocument(
            [CreateAuditEvent()],
            options);
        using var json = JsonDocument.Parse(document.Content);
        var payload = Encoding.UTF8.GetBytes(
            json.RootElement.GetProperty("payload").GetRawText());
        var signature = new AuditArchiveSignature(
            document.SignatureAlgorithm,
            document.Signature);

        Assert.Equal("RSA-PSS-SHA256", document.SignatureAlgorithm);
        Assert.True(ReconciliationAuditArchiveSigner.Verify(payload, signature, options));
        payload[0] ^= 1;
        Assert.False(ReconciliationAuditArchiveSigner.Verify(payload, signature, options));

        using var differentRsa = RSA.Create(2048);
        options.SigningPublicKeyPem = differentRsa.ExportSubjectPublicKeyInfoPem();
        Assert.False(ReconciliationImmutableAuditArchiveOptionsValidator.IsValid(options));
    }

    private static ReconciliationImmutableAuditArchiveOptions CreateOptions() => new()
    {
        Enabled = true,
        BucketName = "audit-archive",
        Prefix = "reconciliation/audit",
        Region = "us-east-1",
        ObjectLockRetentionDays = 3650,
        SigningKeyId = "audit-key-2026",
        SigningKeyBase64 = Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray())
    };

    private static ReconciliationAuditEvent CreateAuditEvent()
    {
        var auditEvent = new ReconciliationAuditEvent
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CreatedAt = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
            ArchivedAt = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero),
            Actor = "admin",
            Action = ReconciliationAuditAction.SourceUpdated,
            ResourceType = ReconciliationAuditResourceType.ReconciliationSource,
            ResourceId = "source-1",
            BeforeStateJson = "{\"enabled\":false}",
            AfterStateJson = "{\"enabled\":true}"
        };
        auditEvent.IntegrityHash = ReconciliationAuditIntegrity.ComputeHash(auditEvent);
        return auditEvent;
    }
}
