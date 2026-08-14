namespace BankingReconciliation.Api.Options;

public sealed class ReconciliationReadinessOptions
{
    public const string SectionName = "ReconciliationReadiness";

    public int TimeoutSeconds { get; set; } = 5;
}
