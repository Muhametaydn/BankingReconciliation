namespace BankingReconciliation.Api.Options;

public static class ReconciliationImmutableAuditArchiveOptionsValidator
{
    public static bool IsRetentionCompatible(
        ReconciliationImmutableAuditArchiveOptions options,
        ReconciliationAuditRetentionOptions retentionOptions) =>
        !options.Enabled ||
        retentionOptions.ArchiveRetentionDays is null ||
        options.ObjectLockRetentionDays >= retentionOptions.ArchiveRetentionDays;

    public static bool IsValid(ReconciliationImmutableAuditArchiveOptions options)
    {
        if (!options.Enabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.BucketName) ||
            string.IsNullOrWhiteSpace(options.Prefix.Trim('/')) ||
            string.IsNullOrWhiteSpace(options.Region) ||
            string.IsNullOrWhiteSpace(options.SigningKeyId) ||
            options.ObjectLockRetentionDays is < 1 or > 36_500 ||
            !Enum.IsDefined(options.SigningAlgorithm) ||
            !HasValidSigningConfiguration(options))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.ExpectedBucketOwner) &&
            (options.ExpectedBucketOwner.Length != 12 ||
                options.ExpectedBucketOwner.Any(character => !char.IsAsciiDigit(character))))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            return true;
        }

        return Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var serviceUri) &&
            (serviceUri.Scheme == Uri.UriSchemeHttp || serviceUri.Scheme == Uri.UriSchemeHttps) &&
            string.IsNullOrEmpty(serviceUri.UserInfo);
    }

    private static bool HasStrongSigningKey(string value)
    {
        try
        {
            return Convert.FromBase64String(value).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool HasValidSigningConfiguration(
        ReconciliationImmutableAuditArchiveOptions options)
    {
        if (options.SigningAlgorithm == ReconciliationAuditSigningAlgorithm.HmacSha256)
        {
            return HasStrongSigningKey(options.SigningKeyBase64) &&
                string.IsNullOrWhiteSpace(options.SigningPrivateKeyPem) &&
                string.IsNullOrWhiteSpace(options.SigningPublicKeyPem) &&
                string.IsNullOrWhiteSpace(options.KmsKeyId);
        }

        if (options.SigningAlgorithm == ReconciliationAuditSigningAlgorithm.AwsKmsRsaPssSha256)
        {
            return string.IsNullOrWhiteSpace(options.SigningKeyBase64) &&
                string.IsNullOrWhiteSpace(options.SigningPrivateKeyPem) &&
                string.IsNullOrWhiteSpace(options.SigningPublicKeyPem) &&
                !string.IsNullOrWhiteSpace(options.KmsKeyId) &&
                !string.IsNullOrWhiteSpace(options.KmsRegion);
        }

        if (!string.IsNullOrWhiteSpace(options.SigningKeyBase64) ||
            !string.IsNullOrWhiteSpace(options.KmsKeyId) ||
            string.IsNullOrWhiteSpace(options.SigningPrivateKeyPem) ||
            string.IsNullOrWhiteSpace(options.SigningPublicKeyPem))
        {
            return false;
        }

        try
        {
            using var privateKey = System.Security.Cryptography.RSA.Create();
            privateKey.ImportFromPem(options.SigningPrivateKeyPem);
            using var publicKey = System.Security.Cryptography.RSA.Create();
            publicKey.ImportFromPem(options.SigningPublicKeyPem);
            var probe = "banking-reconciliation-audit-signing-probe"u8.ToArray();
            var signature = privateKey.SignData(
                probe,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pss);
            return privateKey.KeySize >= 2048 &&
                publicKey.KeySize >= 2048 &&
                publicKey.VerifyData(
                    probe,
                    signature,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.RSASignaturePadding.Pss);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }
}
