namespace BankingReconciliation.Api.Options;

public static class ReconciliationDatabaseSourcesOptionsValidator
{
    private static readonly string[] SupportedCodes = ["BRANCH", "BANK"];

    public static bool IsValid(ReconciliationDatabaseSourcesOptions options)
    {
        if (options.CommandTimeoutSeconds is < 1 or > 300 ||
            options.MaxRecordsPerSource < 1 ||
            options.Sources is null)
        {
            return false;
        }

        var normalizedCodes = options.Sources
            .Select(source => source.Code?.Trim() ?? string.Empty)
            .ToArray();

        return normalizedCodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == normalizedCodes.Length &&
            options.Sources.All(source =>
                SupportedCodes.Contains(source.Code?.Trim(), StringComparer.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(source.ConnectionStringName) &&
                IsReadOnlyQuery(source.Query));
    }

    private static bool IsReadOnlyQuery(string? query)
    {
        var value = query?.TrimStart() ?? string.Empty;
        return value.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }
}
