namespace BankingReconciliation.Api.Options;

public static class ReconciliationComparisonOptionsValidator
{
    private static readonly string[] SupportedMatchingFields =
    [
        "BranchCode",
        "FundCode",
        "TransactionNumber",
        "TransactionDate",
        "Quantity",
        "Amount"
    ];

    private static readonly string[] SupportedComparisonFields =
    [
        "Quantity",
        "Amount"
    ];

    public static bool HasValidMatchingFields(ReconciliationComparisonOptions options)
    {
        return HasValidFields(options.MatchingFields, SupportedMatchingFields);
    }

    public static bool HasValidComparisonFields(ReconciliationComparisonOptions options)
    {
        return HasValidFlexibleFields(options.ComparisonFields);
    }

    public static bool HasValidResultFields(ReconciliationComparisonOptions options)
    {
        return HasValidFlexibleFields(options.ResultFields);
    }

    public static bool HasValidDecimalPlaces(ReconciliationComparisonOptions options)
    {
        return IsValidDecimalPlaces(options.QuantityDecimalPlaces) &&
            IsValidDecimalPlaces(options.BranchQuantityDecimalPlaces) &&
            IsValidDecimalPlaces(options.BankQuantityDecimalPlaces) &&
            IsValidDecimalPlaces(options.AmountDecimalPlaces) &&
            IsValidDecimalPlaces(options.BranchAmountDecimalPlaces) &&
            IsValidDecimalPlaces(options.BankAmountDecimalPlaces);
    }

    public static bool HasValidMappings(ReconciliationComparisonOptions options)
    {
        return IsValidMapping(options.BranchCodeMappings) &&
            IsValidMapping(options.FundCodeMappings) &&
            IsValidMapping(options.TransactionNumberMappings) &&
            options.FieldMappings is not null &&
            options.FieldMappings.All(mapping =>
                !string.IsNullOrWhiteSpace(mapping.Key) && IsValidMapping(mapping.Value));
    }

    public static bool HasValidTolerances(ReconciliationComparisonOptions options)
    {
        return options.QuantityTolerance >= 0 && options.AmountTolerance >= 0;
    }

    public static bool HasFieldsCompatibleWithSchema(
        ReconciliationComparisonOptions options,
        ReconciliationFileSchemaOptions schemaOptions)
    {
        var columns = schemaOptions.GetEffectiveColumns();
        var columnTypes = columns.ToDictionary(
            column => column.Field,
            column => column.Type,
            StringComparer.OrdinalIgnoreCase);

        return options.ComparisonFields.All(field =>
                columnTypes.TryGetValue(field.Trim(), out var type) &&
                (type.Equals("Decimal", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("Integer", StringComparison.OrdinalIgnoreCase))) &&
            options.ResultFields.All(field => columnTypes.ContainsKey(field.Trim()));
    }

    private static bool HasValidFields(string[]? fields, IReadOnlyCollection<string> supportedFields)
    {
        if (fields is null)
        {
            return false;
        }

        if (fields.Length == 0)
        {
            return true;
        }

        var normalizedFields = fields
            .Select(field => field?.Trim() ?? string.Empty)
            .ToArray();

        return normalizedFields.All(field =>
                !string.IsNullOrWhiteSpace(field) &&
                supportedFields.Contains(field, StringComparer.OrdinalIgnoreCase)) &&
            normalizedFields.Distinct(StringComparer.OrdinalIgnoreCase).Count() == normalizedFields.Length;
    }

    private static bool HasValidFlexibleFields(string[]? fields)
    {
        if (fields is null)
        {
            return false;
        }

        if (fields.Length == 0)
        {
            return true;
        }

        var normalizedFields = fields
            .Select(field => field?.Trim() ?? string.Empty)
            .ToArray();

        return normalizedFields.All(field => !string.IsNullOrWhiteSpace(field)) &&
            normalizedFields.Distinct(StringComparer.OrdinalIgnoreCase).Count() == normalizedFields.Length;
    }

    private static bool IsValidDecimalPlaces(int? decimalPlaces)
    {
        return decimalPlaces is null or >= 0 and <= 10;
    }

    private static bool IsValidMapping(IReadOnlyDictionary<string, string>? mappings)
    {
        return mappings is not null && mappings.All(mapping =>
            !string.IsNullOrWhiteSpace(mapping.Key) &&
            !string.IsNullOrWhiteSpace(mapping.Value));
    }
}
