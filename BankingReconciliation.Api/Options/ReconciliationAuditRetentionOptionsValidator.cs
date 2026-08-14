namespace BankingReconciliation.Api.Options;

public static class ReconciliationAuditRetentionOptionsValidator
{
    public static bool IsValid(ReconciliationAuditRetentionOptions options) =>
        options.HotRetentionDays is >= 1 and <= 36_500 &&
        (options.ArchiveRetentionDays is null ||
            options.ArchiveRetentionDays is >= 1 and <= 36_500 &&
            options.ArchiveRetentionDays > options.HotRetentionDays) &&
        options.CleanupIntervalHours is >= 1 and <= 24 * 30 &&
        options.BatchSize is >= 1 and <= 10_000 &&
        options.ExternalArchiveBacklogAlertCount is >= 1 and <= 1_000_000 &&
        options.ExternalArchiveBacklogAlertAgeHours is >= 1 and <= 24 * 365 &&
        options.MaximumRunLatenessHours >= options.CleanupIntervalHours &&
        options.MaximumRunLatenessHours <= 24 * 365;
}
