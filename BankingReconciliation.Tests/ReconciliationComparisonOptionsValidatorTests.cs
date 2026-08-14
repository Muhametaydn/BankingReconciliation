using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Tests;

public class ReconciliationComparisonOptionsValidatorTests
{
    [Fact]
    public void HasValidMatchingFields_ReturnsTrue_WhenMatchingFieldsAreEmpty()
    {
        var options = new ReconciliationComparisonOptions();

        Assert.True(ReconciliationComparisonOptionsValidator.HasValidMatchingFields(options));
    }

    [Fact]
    public void HasValidMatchingFields_ReturnsTrue_WhenFieldsAreSupported()
    {
        var options = new ReconciliationComparisonOptions
        {
            MatchingFields = ["BranchCode", "TransactionDate", "TransactionNumber"]
        };

        Assert.True(ReconciliationComparisonOptionsValidator.HasValidMatchingFields(options));
    }

    [Fact]
    public void HasValidMatchingFields_ReturnsFalse_WhenFieldIsUnsupported()
    {
        var options = new ReconciliationComparisonOptions
        {
            MatchingFields = ["BranchCode", "UnknownField"]
        };

        Assert.False(ReconciliationComparisonOptionsValidator.HasValidMatchingFields(options));
    }

    [Fact]
    public void HasValidMatchingFields_ReturnsFalse_WhenFieldsContainDuplicate()
    {
        var options = new ReconciliationComparisonOptions
        {
            MatchingFields = ["BranchCode", " branchcode "]
        };

        Assert.False(ReconciliationComparisonOptionsValidator.HasValidMatchingFields(options));
    }

    [Fact]
    public void HasValidComparisonFields_ReturnsTrue_WhenComparisonFieldsAreEmpty()
    {
        var options = new ReconciliationComparisonOptions();

        Assert.True(ReconciliationComparisonOptionsValidator.HasValidComparisonFields(options));
    }

    [Fact]
    public void HasValidComparisonFields_ReturnsTrue_WhenFieldsAreSupported()
    {
        var options = new ReconciliationComparisonOptions
        {
            ComparisonFields = ["Quantity", "Amount"]
        };

        Assert.True(ReconciliationComparisonOptionsValidator.HasValidComparisonFields(options));
    }

    [Fact]
    public void HasValidComparisonFields_ReturnsFalse_WhenFieldIsBlank()
    {
        var options = new ReconciliationComparisonOptions
        {
            ComparisonFields = ["Quantity", " "]
        };

        Assert.False(ReconciliationComparisonOptionsValidator.HasValidComparisonFields(options));
    }

    [Fact]
    public void HasValidComparisonFields_ReturnsFalse_WhenFieldsContainDuplicate()
    {
        var options = new ReconciliationComparisonOptions
        {
            ComparisonFields = ["Amount", " amount "]
        };

        Assert.False(ReconciliationComparisonOptionsValidator.HasValidComparisonFields(options));
    }

    [Fact]
    public void HasValidResultFields_ReturnsTrue_WhenFieldsAreSupported()
    {
        var options = new ReconciliationComparisonOptions
        {
            ResultFields = ["BranchCode", "TransactionDate", "Amount"]
        };

        Assert.True(ReconciliationComparisonOptionsValidator.HasValidResultFields(options));
    }

    [Fact]
    public void HasValidResultFields_ReturnsFalse_WhenFieldIsBlank()
    {
        var options = new ReconciliationComparisonOptions
        {
            ResultFields = ["BranchCode", " "]
        };

        Assert.False(ReconciliationComparisonOptionsValidator.HasValidResultFields(options));
    }

    [Fact]
    public void HasValidResultFields_ReturnsFalse_WhenFieldsContainDuplicate()
    {
        var options = new ReconciliationComparisonOptions
        {
            ResultFields = ["Amount", " amount "]
        };

        Assert.False(ReconciliationComparisonOptionsValidator.HasValidResultFields(options));
    }

    [Fact]
    public void HasValidDecimalPlaces_ReturnsFalse_WhenPrecisionExceedsLimit()
    {
        var options = new ReconciliationComparisonOptions
        {
            AmountDecimalPlaces = 11
        };

        Assert.False(ReconciliationComparisonOptionsValidator.HasValidDecimalPlaces(options));
    }

    [Fact]
    public void HasValidMappings_ReturnsFalse_WhenTargetIsBlank()
    {
        var options = new ReconciliationComparisonOptions
        {
            FundCodeMappings = new Dictionary<string, string>
            {
                ["A FONU"] = " "
            }
        };

        Assert.False(ReconciliationComparisonOptionsValidator.HasValidMappings(options));
    }

    [Fact]
    public void HasFieldsCompatibleWithSchema_ReturnsTrue_ForExtraNumericField()
    {
        var schema = new ReconciliationFileSchemaOptions
        {
            Columns =
            [
                .. ReconciliationFileSchemaOptions.GetDefaultColumns(),
                new ReconciliationFileSchemaColumnOptions
                {
                    Field = "Commission",
                    Name = "Commission",
                    Type = "Decimal"
                }
            ]
        };
        var options = new ReconciliationComparisonOptions
        {
            ComparisonFields = ["Quantity", "Commission"],
            ResultFields = ["BranchCode", "Commission"]
        };

        Assert.True(ReconciliationComparisonOptionsValidator.HasFieldsCompatibleWithSchema(options, schema));
    }

    [Fact]
    public void HasFieldsCompatibleWithSchema_ReturnsFalse_ForUnknownField()
    {
        var options = new ReconciliationComparisonOptions
        {
            ComparisonFields = ["UnknownField"]
        };

        Assert.False(ReconciliationComparisonOptionsValidator.HasFieldsCompatibleWithSchema(
            options,
            new ReconciliationFileSchemaOptions()));
    }
}
