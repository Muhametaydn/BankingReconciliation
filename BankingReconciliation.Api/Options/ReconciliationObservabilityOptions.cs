namespace BankingReconciliation.Api.Options;

public sealed class ReconciliationObservabilityOptions
{
    public const string SectionName = "ReconciliationObservability";

    public bool OpenTelemetryEnabled { get; set; }
    public string OtlpEndpoint { get; set; } = string.Empty;
}
