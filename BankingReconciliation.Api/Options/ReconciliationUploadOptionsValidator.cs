namespace BankingReconciliation.Api.Options;

public static class ReconciliationUploadOptionsValidator
{
    public static bool HasValidTemporaryStorage(ReconciliationUploadOptions options)
    {
        return options.TemporaryStorageMode switch
        {
            ReconciliationTemporaryStorageMode.Local => true,
            ReconciliationTemporaryStorageMode.SharedFileSystem =>
                !string.IsNullOrWhiteSpace(options.TemporaryStoragePath) &&
                Path.IsPathFullyQualified(options.TemporaryStoragePath),
            ReconciliationTemporaryStorageMode.S3Compatible => HasValidS3Configuration(options),
            _ => false
        };
    }

    private static bool HasValidS3Configuration(ReconciliationUploadOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.S3BucketName) ||
            string.IsNullOrWhiteSpace(options.S3Prefix.Trim('/')) ||
            string.IsNullOrWhiteSpace(options.S3Region) ||
            !Enum.IsDefined(options.S3ServerSideEncryption))
        {
            return false;
        }

        var usesKms =
            options.S3ServerSideEncryption == ReconciliationS3ServerSideEncryption.AwsKms;
        if (usesKms != !string.IsNullOrWhiteSpace(options.S3KmsKeyId))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.S3ExpectedBucketOwner) &&
            (options.S3ExpectedBucketOwner.Length != 12 ||
                options.S3ExpectedBucketOwner.Any(character => !char.IsAsciiDigit(character))))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.S3ServiceUrl))
        {
            return true;
        }

        return Uri.TryCreate(options.S3ServiceUrl, UriKind.Absolute, out var serviceUri) &&
            (string.Equals(serviceUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(serviceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
            string.IsNullOrEmpty(serviceUri.UserInfo);
    }
}
