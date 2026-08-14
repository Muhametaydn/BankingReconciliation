using System.Security.Cryptography;
using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Api.Services;

internal static class ReconciliationAuditArchiveSigner
{
    public static AuditArchiveSignature Sign(
        ReadOnlySpan<byte> payload,
        ReconciliationImmutableAuditArchiveOptions options)
    {
        if (options.SigningAlgorithm == ReconciliationAuditSigningAlgorithm.RsaPssSha256)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(options.SigningPrivateKeyPem);
            return new AuditArchiveSignature(
                "RSA-PSS-SHA256",
                Convert.ToBase64String(rsa.SignData(
                    payload,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss)));
        }

        using var hmac = new HMACSHA256(Convert.FromBase64String(options.SigningKeyBase64));
        return new AuditArchiveSignature(
            "HMAC-SHA256",
            Convert.ToBase64String(hmac.ComputeHash(payload.ToArray())));
    }

    public static bool Verify(
        ReadOnlySpan<byte> payload,
        AuditArchiveSignature signature,
        ReconciliationImmutableAuditArchiveOptions options)
    {
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signature.Value);
        }
        catch (FormatException)
        {
            return false;
        }

        if (options.SigningAlgorithm == ReconciliationAuditSigningAlgorithm.RsaPssSha256)
        {
            if (!string.Equals(signature.Algorithm, "RSA-PSS-SHA256", StringComparison.Ordinal))
            {
                return false;
            }
            using var rsa = RSA.Create();
            rsa.ImportFromPem(options.SigningPublicKeyPem);
            return rsa.VerifyData(
                payload,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
        }

        if (!string.Equals(signature.Algorithm, "HMAC-SHA256", StringComparison.Ordinal))
        {
            return false;
        }
        using var hmac = new HMACSHA256(Convert.FromBase64String(options.SigningKeyBase64));
        return CryptographicOperations.FixedTimeEquals(
            hmac.ComputeHash(payload.ToArray()),
            signatureBytes);
    }
}

internal sealed record AuditArchiveSignature(string Algorithm, string Value);
