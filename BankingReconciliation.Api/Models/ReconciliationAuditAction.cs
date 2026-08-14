namespace BankingReconciliation.Api.Models;

public enum ReconciliationAuditAction
{
    ReconciliationApproved,
    ReconciliationRejected,
    SourceUpdated,
    FileSchemaUpdated,
    ComparisonSettingsUpdated
}
