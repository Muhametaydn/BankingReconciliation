using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Data;

public class ReconciliationAuditEventEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public ReconciliationAuditAction Action { get; set; }
    public ReconciliationAuditResourceType ResourceType { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public string? BeforeStateJson { get; set; }
    public string? AfterStateJson { get; set; }
}
