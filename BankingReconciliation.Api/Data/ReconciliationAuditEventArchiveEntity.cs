using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Data;

public class ReconciliationAuditEventArchiveEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ArchivedAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public ReconciliationAuditAction Action { get; set; }
    public ReconciliationAuditResourceType ResourceType { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public string? BeforeStateJson { get; set; }
    public string? AfterStateJson { get; set; }
    public string IntegrityHash { get; set; } = string.Empty;
    public DateTimeOffset? ExternalArchivedAt { get; set; }
    public string? ExternalArchiveKey { get; set; }
}
