namespace BankingReconciliation.Api.Options;

public sealed class ReconciliationProductionOptions
{
    public const string SectionName = "ReconciliationProduction";

    public string DeploymentVersion { get; set; } = string.Empty;
    public bool ApplyDatabaseMigrationsOnStartup { get; set; }
    public int RateLimitPermitCount { get; set; } = 120;
    public int RateLimitWindowSeconds { get; set; } = 60;
    public int RateLimitQueueCount { get; set; } = 20;
    public string[] KnownProxyNetworks { get; set; } = [];
}
