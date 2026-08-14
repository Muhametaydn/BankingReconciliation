using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Tests;

public class ReconciliationFileSchemaOptionsValidatorTests
{
    [Fact]
    public void HasValidColumnDefinitions_ReturnsFalse_WhenFixedWidthDefinitionIsPartial()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        options.Columns[0].FixedWidthStart = 1;
        options.Columns[0].FixedWidthLength = 20;

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
    }

    [Fact]
    public void HasValidColumnDefinitions_ReturnsFalse_WhenFixedWidthColumnsOverlap()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        var start = 1;
        foreach (var column in options.Columns)
        {
            column.FixedWidthStart = start;
            column.FixedWidthLength = 20;
            start += 20;
        }
        options.Columns[1].FixedWidthStart = 10;

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
    }

    [Fact]
    public void DefaultSchema_IsValid()
    {
        var options = new ReconciliationFileSchemaOptions();

        Assert.True(ReconciliationFileSchemaOptionsValidator.HasRequiredTransactionFields(options));
        Assert.True(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
        Assert.True(ReconciliationFileSchemaOptionsValidator.HasUniqueColumnNames(options));
        Assert.True(ReconciliationFileSchemaOptionsValidator.HasUniqueFieldNames(options));
    }

    [Fact]
    public void HasUniqueColumnNames_ReturnsFalse_ForCaseInsensitiveDuplicate()
    {
        var options = CreateOptions();
        options.Columns[1].Name = " branchcode ";

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasUniqueColumnNames(options));
    }

    [Fact]
    public void HasRequiredTransactionFields_ReturnsFalse_WhenFieldIsMissing()
    {
        var options = CreateOptions();
        options.Columns = options.Columns[..^1];

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasRequiredTransactionFields(options));
    }

    [Fact]
    public void HasRequiredTransactionFields_ReturnsTrue_WhenSchemaContainsExtraField()
    {
        var options = CreateOptions();
        options.Columns =
        [
            .. options.Columns,
            new()
            {
                Field = "Commission",
                Name = "Commission",
                Type = "Decimal",
                Required = false
            }
        ];

        Assert.True(ReconciliationFileSchemaOptionsValidator.HasRequiredTransactionFields(options));
        Assert.True(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
        Assert.True(ReconciliationFileSchemaOptionsValidator.HasUniqueFieldNames(options));
    }

    [Fact]
    public void HasUniqueFieldNames_ReturnsFalse_ForCaseInsensitiveDuplicate()
    {
        var options = CreateOptions();
        options.Columns =
        [
            .. options.Columns,
            new()
            {
                Field = " branchcode ",
                Name = "BranchCodeCopy",
                Type = "Text",
                Required = false
            }
        ];

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasUniqueFieldNames(options));
    }

    [Fact]
    public void HasValidColumnDefinitions_ReturnsFalse_WhenDateFormatIsMissing()
    {
        var options = CreateOptions();
        var dateColumn = Assert.Single(options.Columns, column => column.Field == "TransactionDate");
        dateColumn.DateFormat = null;

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
    }

    [Fact]
    public void HasValidColumnDefinitions_ReturnsTrue_ForIntegerType()
    {
        var options = CreateOptions();
        var transactionNumberColumn = Assert.Single(
            options.Columns,
            column => column.Field == "TransactionNumber");
        transactionNumberColumn.Type = "Integer";

        Assert.True(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
    }

    [Fact]
    public void HasValidColumnDefinitions_ReturnsFalse_WhenPatternIsInvalid()
    {
        var options = CreateOptions();
        var transactionNumberColumn = Assert.Single(
            options.Columns,
            column => column.Field == "TransactionNumber");
        transactionNumberColumn.Pattern = "[";

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
    }

    [Fact]
    public void HasValidColumnDefinitions_ReturnsFalse_WhenLengthRangeIsInvalid()
    {
        var options = CreateOptions();
        var transactionNumberColumn = Assert.Single(
            options.Columns,
            column => column.Field == "TransactionNumber");
        transactionNumberColumn.MinLength = 8;
        transactionNumberColumn.MaxLength = 4;

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
    }

    [Fact]
    public void HasValidColumnDefinitions_ReturnsFalse_WhenAllowedValuesContainDuplicates()
    {
        var options = CreateOptions();
        var fundCodeColumn = Assert.Single(
            options.Columns,
            column => column.Field == "FundCode");
        fundCodeColumn.AllowedValues = ["A", " a "];

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
    }

    [Fact]
    public void HasValidColumnDefinitions_ReturnsFalse_WhenNumericRangeIsInvalid()
    {
        var options = CreateOptions();
        var quantityColumn = Assert.Single(
            options.Columns,
            column => column.Field == "Quantity");
        quantityColumn.MinValue = 10;
        quantityColumn.MaxValue = 5;

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void HasValidColumnDefinitions_ReturnsFalse_WhenMaxDecimalPlacesIsInvalid(int maxDecimalPlaces)
    {
        var options = CreateOptions();
        var amountColumn = Assert.Single(
            options.Columns,
            column => column.Field == "Amount");
        amountColumn.MaxDecimalPlaces = maxDecimalPlaces;

        Assert.False(ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(options));
    }

    private static ReconciliationFileSchemaOptions CreateOptions()
    {
        return new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
    }
}
