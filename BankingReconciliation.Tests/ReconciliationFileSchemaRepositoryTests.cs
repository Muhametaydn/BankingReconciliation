using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;

namespace BankingReconciliation.Tests;

public class ReconciliationFileSchemaRepositoryTests
{
    [Fact]
    public void SaveAndGet_RoundTripsSchemaWithoutSharingMutableState()
    {
        var repository = new InMemoryReconciliationFileSchemaRepository();
        var options = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        options.Columns[0].Name = "Branch";

        repository.Save(options);
        options.Columns[0].Name = "ChangedAfterSave";

        var stored = repository.Get();

        Assert.NotNull(stored);
        Assert.Equal("Branch", stored.Columns[0].Name);

        stored.Columns[0].Name = "ChangedAfterRead";
        Assert.Equal("Branch", repository.Get()!.Columns[0].Name);
    }


    [Fact]
    public void ComparisonOptionsSaveAndGet_RoundTripsWithoutSharingMutableState()
    {
        var repository = new InMemoryReconciliationComparisonOptionsRepository();
        var options = new ReconciliationComparisonOptions
        {
            MatchingFields = ["BranchCode", "TransactionNumber"],
            FieldMappings = new Dictionary<string, Dictionary<string, string>>
            {
                ["FundCode"] = new() { ["A FONU"] = "A" }
            }
        };

        repository.Save(options);
        options.MatchingFields[0] = "ChangedAfterSave";
        options.FieldMappings["FundCode"]["A FONU"] = "ChangedAfterSave";

        var stored = repository.Get();

        Assert.NotNull(stored);
        Assert.Equal("BranchCode", stored.MatchingFields[0]);
        Assert.Equal("A", stored.FieldMappings["FundCode"]["A FONU"]);
    }
}
