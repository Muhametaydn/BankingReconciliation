namespace BankingReconciliation.Api.Models;

public class ReconciliationAuditEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public ReconciliationAuditAction Action { get; set; }
    public ReconciliationAuditResourceType ResourceType { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public string? BeforeStateJson { get; set; }
    public string? AfterStateJson { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? IntegrityHash { get; set; }
    public bool? IntegrityVerified { get; set; }
    public DateTimeOffset? ExternalArchivedAt { get; set; }
    public string? ExternalArchiveKey { get; set; }
}
