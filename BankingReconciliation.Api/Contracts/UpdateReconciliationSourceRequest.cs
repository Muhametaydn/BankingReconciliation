namespace BankingReconciliation.Api.Contracts;

public class UpdateReconciliationSourceRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
