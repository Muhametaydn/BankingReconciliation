namespace BankingReconciliation.Api.Options;

public sealed class ReconciliationJobOptions
{
    public const string SectionName = "ReconciliationJobs";

    public int LeaseDurationSeconds { get; set; } = 120;
    public int LeaseRenewalSeconds { get; set; } = 30;
    public int PollIntervalMilliseconds { get; set; } = 2000;
    public int MaxAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
}
