namespace BankingReconciliation.Api.Contracts;

public sealed class ReconciliationAuditRetentionStatusResponse
{
    public string Status { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool ImmutableArchiveEnabled { get; set; }
    public int HotRetentionDays { get; set; }
    public int? ArchiveRetentionDays { get; set; }
    public int BatchSize { get; set; }
    public int HotEventCount { get; set; }
    public int ArchivedEventCount { get; set; }
    public int PendingExternalArchiveCount { get; set; }
    public DateTimeOffset? OldestPendingExternalArchiveAt { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }
    public int LastArchivedCount { get; set; }
    public int LastPurgedCount { get; set; }
    public int LastExternalArchivedCount { get; set; }
    public bool Alerting { get; set; }
    public IReadOnlyList<string> Alerts { get; set; } = [];
}
