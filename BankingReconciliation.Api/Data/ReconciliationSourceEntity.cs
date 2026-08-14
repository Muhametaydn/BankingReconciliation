using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Data;

public class ReconciliationSourceEntity
{
    public Guid Id { get; set; }
    public ReconciliationSourceType Type { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
