using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public class ReconciliationAuditQuery
{
    public string? Actor { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public ReconciliationAuditAction? Action { get; set; }
    public ReconciliationAuditResourceType? ResourceType { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}
