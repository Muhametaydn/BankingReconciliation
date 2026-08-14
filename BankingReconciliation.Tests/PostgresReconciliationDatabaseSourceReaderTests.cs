using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class PostgresReconciliationDatabaseSourceReaderTests
{
    private const string ConnectionStringEnvironmentVariable =
        "BANKING_RECONCILIATION_POSTGRES_TEST_CONNECTION";

    [Fact]
    public async Task ReadAsync_ThrowsSourceError_WhenSourceIsNotConfigured()
    {
        var reader = CreateReader(
            new ConfigurationBuilder().Build(),
            new ReconciliationDatabaseSourcesOptions());

        var exception = await Assert.ThrowsAsync<ReconciliationDatabaseSourceException>(() =>
            reader.ReadAsync("BRANCH"));

        Assert.Equal("BRANCH", exception.SourceCode);
        Assert.Contains("not configured", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_ReadsAndNormalizesRows_WhenPostgresIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SourceReadTestDatabase"] = connectionString
            })
            .Build();
        var databaseOptions = new ReconciliationDatabaseSourcesOptions
        {
            Sources =
            [
                new ReconciliationDatabaseSourceOptions
                {
                    Code = "BRANCH",
                    ConnectionStringName = "SourceReadTestDatabase",
                    Query = """
                        SELECT
                            'beylikduzu sube'::text AS "BranchCode",
                            'A'::text AS "FundCode",
                            'TX-001'::text AS "TransactionNumber",
                            DATE '2026-06-26' AS "TransactionDate",
                            100.25::numeric AS "Quantity",
                            10000.50::numeric AS "Amount",
                            12.34::numeric AS "Commission"
                        """
                }
            ]
        };
        var schema = new ReconciliationFileSchemaOptions
        {
            Columns =
            [
                .. ReconciliationFileSchemaOptions.GetDefaultColumns(),
                new ReconciliationFileSchemaColumnOptions
                {
                    Field = "Commission",
                    Name = "Commission",
                    Type = "Decimal",
                    Required = false
                }
            ]
        };
        var comparisonOptions = new ReconciliationComparisonOptions
        {
            NormalizeCodeCase = true,
            TrimTextValues = true,
            BranchCodeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BEYLIKDUZU SUBE"] = "BEYLIKDUZU"
            },
            TransactionNumberMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TX-001"] = "TX001"
            }
        };
        var reader = CreateReader(configuration, databaseOptions, schema, comparisonOptions);

        var records = await reader.ReadAsync("BRANCH");

        var record = Assert.Single(records);
        Assert.Equal("BEYLIKDUZU", record.BranchCode);
        Assert.Equal("TX001", record.TransactionNumber);
        Assert.Equal(new DateOnly(2026, 6, 26), record.TransactionDate);
        Assert.Equal(100.25m, record.Quantity);
        Assert.Equal(10000.50m, record.Amount);
        Assert.Equal("12.34", record.ExtraFields["Commission"]);
    }

    private static PostgresReconciliationDatabaseSourceReader CreateReader(
        IConfiguration configuration,
        ReconciliationDatabaseSourcesOptions databaseOptions,
        ReconciliationFileSchemaOptions? schema = null,
        ReconciliationComparisonOptions? comparisonOptions = null)
    {
        return new PostgresReconciliationDatabaseSourceReader(
            configuration,
            Options.Create(databaseOptions),
            new ReconciliationFileSchemaStore(Options.Create(
                schema ?? new ReconciliationFileSchemaOptions())),
            new ReconciliationComparisonOptionsStore(Options.Create(
                comparisonOptions ?? new ReconciliationComparisonOptions())));
    }
}
