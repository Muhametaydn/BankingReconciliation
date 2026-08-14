namespace BankingReconciliation.Api.Options;

public class ReconciliationAuditRetentionOptions
{
    public const string SectionName = "ReconciliationAuditRetention";

    public bool Enabled { get; set; } = true;
    public int HotRetentionDays { get; set; } = 365;
    public int? ArchiveRetentionDays { get; set; } = 2555;
    public int CleanupIntervalHours { get; set; } = 24;
    public int BatchSize { get; set; } = 500;
    public int ExternalArchiveBacklogAlertCount { get; set; } = 500;
    public int ExternalArchiveBacklogAlertAgeHours { get; set; } = 24;
    public int MaximumRunLatenessHours { get; set; } = 48;
}
