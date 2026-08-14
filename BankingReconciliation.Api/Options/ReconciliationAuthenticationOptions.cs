namespace BankingReconciliation.Api.Options;

public class ReconciliationAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public bool RequireHttpsMetadata { get; set; } = true;
    public int ClockSkewSeconds { get; set; } = 60;
    public string NameClaimType { get; set; } = "name";
    public string RoleClaimType { get; set; } = "role";
    public string ApproverRole { get; set; } = "ReconciliationApprover";
    public string PermissionClaimType { get; set; } = "permission";
    public string ApproverPermission { get; set; } = "reconciliation.approve";
    public string AdministratorRole { get; set; } = "ReconciliationAdministrator";
    public string AdministratorPermission { get; set; } = "reconciliation.manage";
    public string DemoSigningKey { get; set; } = string.Empty;
}
