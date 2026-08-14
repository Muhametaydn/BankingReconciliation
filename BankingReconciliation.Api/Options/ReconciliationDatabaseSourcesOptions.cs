namespace BankingReconciliation.Api.Options;

public class ReconciliationDatabaseSourcesOptions
{
    public const string SectionName = "ReconciliationDatabaseSources";

    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MaxRecordsPerSource { get; set; } = 100_000;
    public ReconciliationDatabaseSourceOptions[] Sources { get; set; } = [];
}

public class ReconciliationDatabaseSourceOptions
{
    public string Code { get; set; } = string.Empty;
    public string ConnectionStringName { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
}
