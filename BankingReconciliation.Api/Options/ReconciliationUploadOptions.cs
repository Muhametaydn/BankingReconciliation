namespace BankingReconciliation.Api.Options;

public class ReconciliationUploadOptions
{
    public const string SectionName = "ReconciliationUpload";

    public long MaxCsvFileSizeBytes { get; set; } = 5 * 1024 * 1024;
    public long SynchronousComparisonMaxFileSizeBytes { get; set; } = 1024 * 1024;
    public int MaxRecordsPerFile { get; set; } = 100_000;
    public int BackgroundQueueCapacity { get; set; } = 100;
    public ReconciliationTemporaryStorageMode TemporaryStorageMode { get; set; } =
        ReconciliationTemporaryStorageMode.Local;
    public string TemporaryStoragePath { get; set; } = string.Empty;
    public int TemporaryFileRetentionHours { get; set; } = 24;
    public int TemporaryFileCleanupIntervalMinutes { get; set; } = 60;
    public int TemporaryFileCleanupBatchSize { get; set; } = 100;
    public string S3BucketName { get; set; } = string.Empty;
    public string S3Prefix { get; set; } = "banking-reconciliation/uploads";
    public string S3Region { get; set; } = "us-east-1";
    public string S3ServiceUrl { get; set; } = string.Empty;
    public bool S3ForcePathStyle { get; set; }
    public ReconciliationS3ServerSideEncryption S3ServerSideEncryption { get; set; } =
        ReconciliationS3ServerSideEncryption.BucketDefault;
    public string S3KmsKeyId { get; set; } = string.Empty;
    public string S3ExpectedBucketOwner { get; set; } = string.Empty;
    public string[] AllowedFileExtensions { get; set; } = [".csv", ".txt"];
}

public enum ReconciliationTemporaryStorageMode
{
    Local,
    SharedFileSystem,
    S3Compatible
}

public enum ReconciliationS3ServerSideEncryption
{
    BucketDefault,
    AES256,
    AwsKms
}
