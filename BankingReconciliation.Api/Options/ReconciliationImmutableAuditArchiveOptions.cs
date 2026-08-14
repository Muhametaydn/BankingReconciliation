namespace BankingReconciliation.Api.Options;

public class ReconciliationImmutableAuditArchiveOptions
{
    public const string SectionName = "ReconciliationImmutableAuditArchive";

    public bool Enabled { get; set; }
    public string BucketName { get; set; } = string.Empty;
    public string Prefix { get; set; } = "banking-reconciliation/audit-archive";
    public string Region { get; set; } = "us-east-1";
    public string ServiceUrl { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; }
    public string ExpectedBucketOwner { get; set; } = string.Empty;
    public int ObjectLockRetentionDays { get; set; } = 3650;
    public ReconciliationAuditSigningAlgorithm SigningAlgorithm { get; set; } =
        ReconciliationAuditSigningAlgorithm.HmacSha256;
    public string SigningKeyId { get; set; } = string.Empty;
    public string SigningKeyBase64 { get; set; } = string.Empty;
    public string SigningPrivateKeyPem { get; set; } = string.Empty;
    public string SigningPublicKeyPem { get; set; } = string.Empty;
    public string KmsKeyId { get; set; } = string.Empty;
    public string KmsRegion { get; set; } = "us-east-1";
}

public enum ReconciliationAuditSigningAlgorithm
{
    HmacSha256,
    RsaPssSha256,
    AwsKmsRsaPssSha256
}
