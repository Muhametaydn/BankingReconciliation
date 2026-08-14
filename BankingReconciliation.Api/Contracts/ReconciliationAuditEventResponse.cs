using System.Text.Json;
using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Contracts;

public class ReconciliationAuditEventResponse
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public ReconciliationAuditAction Action { get; set; }
    public ReconciliationAuditResourceType ResourceType { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public JsonElement? BeforeState { get; set; }
    public JsonElement? AfterState { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? IntegrityHash { get; set; }
    public bool? IntegrityVerified { get; set; }
    public DateTimeOffset? ExternalArchivedAt { get; set; }
    public string? ExternalArchiveKey { get; set; }
}
