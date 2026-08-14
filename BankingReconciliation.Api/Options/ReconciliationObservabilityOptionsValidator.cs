namespace BankingReconciliation.Api.Options;

public static class ReconciliationObservabilityOptionsValidator
{
    public static bool IsValid(ReconciliationObservabilityOptions options)
    {
        if (!options.OpenTelemetryEnabled)
        {
            return true;
        }

        return Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out var endpoint) &&
            endpoint.Scheme is "http" or "https";
    }
}
