using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Tests;

public class ReconciliationDatabaseSourcesOptionsValidatorTests
{
    [Fact]
    public void IsValid_ReturnsTrue_WhenNoDatabaseSourcesAreConfigured()
    {
        Assert.True(ReconciliationDatabaseSourcesOptionsValidator.IsValid(
            new ReconciliationDatabaseSourcesOptions()));
    }

    [Fact]
    public void IsValid_ReturnsTrue_ForNamedConnectionAndReadOnlyQuery()
    {
        var options = CreateOptions("SELECT * FROM transactions");

        Assert.True(ReconciliationDatabaseSourcesOptionsValidator.IsValid(options));
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForWriteQuery()
    {
        var options = CreateOptions("DELETE FROM transactions");

        Assert.False(ReconciliationDatabaseSourcesOptionsValidator.IsValid(options));
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForDuplicateSourceCode()
    {
        var options = CreateOptions("SELECT * FROM transactions");
        options.Sources = [options.Sources[0], options.Sources[0]];

        Assert.False(ReconciliationDatabaseSourcesOptionsValidator.IsValid(options));
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForNonPositiveRecordLimit()
    {
        var options = CreateOptions("SELECT * FROM transactions");
        options.MaxRecordsPerSource = 0;

        Assert.False(ReconciliationDatabaseSourcesOptionsValidator.IsValid(options));
    }

    private static ReconciliationDatabaseSourcesOptions CreateOptions(string query)
    {
        return new ReconciliationDatabaseSourcesOptions
        {
            Sources =
            [
                new ReconciliationDatabaseSourceOptions
                {
                    Code = "BRANCH",
                    ConnectionStringName = "BranchSourceDatabase",
                    Query = query
                }
            ]
        };
    }
}
