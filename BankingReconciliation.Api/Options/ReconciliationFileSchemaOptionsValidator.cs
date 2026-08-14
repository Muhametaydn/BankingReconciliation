using System.Text.RegularExpressions;

namespace BankingReconciliation.Api.Options;

public static class ReconciliationFileSchemaOptionsValidator
{
    private static readonly string[] RequiredFields =
    [
        "BranchCode",
        "FundCode",
        "TransactionNumber",
        "TransactionDate",
        "Quantity",
        "Amount"
    ];

    public static bool HasRequiredTransactionFields(ReconciliationFileSchemaOptions options)
    {
        var columns = options.GetEffectiveColumns();

        return columns.Length >= RequiredFields.Length &&
            RequiredFields.All(requiredField =>
                columns.Count(column =>
                    string.Equals(column.Field, requiredField, StringComparison.OrdinalIgnoreCase)) == 1);
    }

    public static bool HasValidColumnDefinitions(ReconciliationFileSchemaOptions options)
    {
        var columns = options.GetEffectiveColumns();

        return HasValidFixedWidthDefinitions(columns) && columns.All(column =>
            !string.IsNullOrWhiteSpace(column.Field) &&
            !string.IsNullOrWhiteSpace(column.Name) &&
            Enum.TryParse(column.Type, ignoreCase: true, out TransactionColumnType _) &&
            (!string.Equals(column.Type, "Date", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(column.DateFormat)) &&
            IsValidPattern(column.Pattern) &&
            IsValidLengthRange(column.MinLength, column.MaxLength) &&
            IsValidValueRange(column.MinValue, column.MaxValue) &&
            IsValidMaxDecimalPlaces(column.MaxDecimalPlaces) &&
            HasValidAllowedValues(column.AllowedValues));
    }

    private static bool HasValidFixedWidthDefinitions(
        IReadOnlyCollection<ReconciliationFileSchemaColumnOptions> columns)
    {
        var configuredColumns = columns
            .Where(column => column.FixedWidthStart is not null || column.FixedWidthLength is not null)
            .ToArray();
        if (configuredColumns.Length == 0)
        {
            return true;
        }

        if (configuredColumns.Length != columns.Count || configuredColumns.Any(column =>
                column.FixedWidthStart < 1 ||
                column.FixedWidthLength < 1 ||
                column.Name.Trim().Length > column.FixedWidthLength))
        {
            return false;
        }

        var orderedColumns = configuredColumns.OrderBy(column => column.FixedWidthStart).ToArray();
        return orderedColumns.Zip(orderedColumns.Skip(1), (current, next) =>
                current.FixedWidthStart!.Value + current.FixedWidthLength!.Value <= next.FixedWidthStart)
            .All(isValid => isValid);
    }

    public static bool HasUniqueColumnNames(ReconciliationFileSchemaOptions options)
    {
        var columnNames = options.GetEffectiveColumns()
            .Select(column => column.Name?.Trim() ?? string.Empty)
            .ToArray();

        return columnNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == columnNames.Length;
    }

    public static bool HasUniqueFieldNames(ReconciliationFileSchemaOptions options)
    {
        var fieldNames = options.GetEffectiveColumns()
            .Select(column => column.Field?.Trim() ?? string.Empty)
            .ToArray();

        return fieldNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == fieldNames.Length;
    }

    private enum TransactionColumnType
    {
        Text,
        Date,
        Decimal,
        Integer
    }

    private static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        try
        {
            _ = new Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValidLengthRange(int? minLength, int? maxLength)
    {
        if (minLength is < 0 || maxLength is < 0)
        {
            return false;
        }

        return minLength is null ||
            maxLength is null ||
            minLength <= maxLength;
    }

    private static bool HasValidAllowedValues(string[]? allowedValues)
    {
        if (allowedValues is null || allowedValues.Length == 0)
        {
            return true;
        }

        var normalizedValues = allowedValues
            .Select(value => value?.Trim() ?? string.Empty)
            .ToArray();

        return normalizedValues.All(value => !string.IsNullOrWhiteSpace(value)) &&
            normalizedValues.Distinct(StringComparer.OrdinalIgnoreCase).Count() == normalizedValues.Length;
    }

    private static bool IsValidValueRange(decimal? minValue, decimal? maxValue)
    {
        return minValue is null ||
            maxValue is null ||
            minValue <= maxValue;
    }

    private static bool IsValidMaxDecimalPlaces(int? maxDecimalPlaces)
    {
        return maxDecimalPlaces is null or >= 0 and <= 10;
    }
}
