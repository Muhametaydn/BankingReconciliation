using Npgsql;

namespace BankingReconciliation.Api.Options;

public static class ReconciliationProductionReadinessValidator
{
    private static readonly string[] UnsafeSecretValues =
        ["root", "password", "changeme", "change-me", "secret"];

    public static bool IsProductionLike(string environmentName) =>
        string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Validate(
        string environmentName,
        string? reconciliationConnectionString,
        string? allowedHosts,
        ReconciliationAuthenticationOptions authentication,
        ReconciliationUploadOptions upload,
        ReconciliationObservabilityOptions observability,
        ReconciliationProductionOptions production)
    {
        if (!IsProductionLike(environmentName))
        {
            return [];
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(reconciliationConnectionString))
        {
            errors.Add("ConnectionStrings:ReconciliationDatabase is required.");
        }
        else if (!TryValidateDatabaseConnection(reconciliationConnectionString, out var databaseError))
        {
            errors.Add(databaseError);
        }

        if (!Uri.TryCreate(authentication.Authority, UriKind.Absolute, out var authority) ||
            authority.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Authentication:Authority must be an absolute HTTPS URL.");
        }
        if (string.IsNullOrWhiteSpace(authentication.Audience))
        {
            errors.Add("Authentication:Audience is required.");
        }
        if (!authentication.RequireHttpsMetadata)
        {
            errors.Add("Authentication:RequireHttpsMetadata must be true.");
        }
        if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Trim() == "*")
        {
            errors.Add("AllowedHosts must contain explicit production host names.");
        }
        if (upload.TemporaryStorageMode == ReconciliationTemporaryStorageMode.Local)
        {
            errors.Add("ReconciliationUpload:TemporaryStorageMode must use persistent shared or object storage.");
        }
        if (!observability.OpenTelemetryEnabled)
        {
            errors.Add("ReconciliationObservability:OpenTelemetryEnabled must be true.");
        }
        if (string.IsNullOrWhiteSpace(production.DeploymentVersion))
        {
            errors.Add("ReconciliationProduction:DeploymentVersion is required.");
        }
        if (production.ApplyDatabaseMigrationsOnStartup)
        {
            errors.Add("ReconciliationProduction:ApplyDatabaseMigrationsOnStartup must be false.");
        }
        if (production.KnownProxyNetworks.Length == 0)
        {
            errors.Add("ReconciliationProduction:KnownProxyNetworks must contain at least one trusted proxy network.");
        }

        return errors;
    }

    public static bool HasValidRuntimeOptions(ReconciliationProductionOptions options) =>
        options.RateLimitPermitCount is >= 1 and <= 100_000 &&
        options.RateLimitWindowSeconds is >= 1 and <= 3600 &&
        options.RateLimitQueueCount is >= 0 and <= 10_000 &&
        options.KnownProxyNetworks.All(IsValidCidr);

    private static bool TryValidateDatabaseConnection(
        string connectionString,
        out string error)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.Password) ||
                UnsafeSecretValues.Contains(builder.Password.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                error = "ConnectionStrings:ReconciliationDatabase contains an unsafe placeholder password.";
                return false;
            }
            if (builder.SslMode != SslMode.VerifyFull)
            {
                error = "ConnectionStrings:ReconciliationDatabase must use SSL Mode=VerifyFull.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (ArgumentException)
        {
            error = "ConnectionStrings:ReconciliationDatabase is not a valid PostgreSQL connection string.";
            return false;
        }
    }

    private static bool IsValidCidr(string value)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var address) ||
            !int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        var maximumPrefix = address.AddressFamily ==
            System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        return prefixLength is >= 0 && prefixLength <= maximumPrefix;
    }
}
