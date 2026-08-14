namespace BankingReconciliation.Api.Models;

public enum ReconciliationAuditAction
{
    ReconciliationApproved,
    ReconciliationRejected,
    UserRegistered,
    UserRoleUpdated,
    SourceUpdated,
    FileSchemaUpdated,
    ComparisonSettingsUpdated
}
