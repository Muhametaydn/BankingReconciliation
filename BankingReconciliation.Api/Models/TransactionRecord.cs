namespace BankingReconciliation.Api.Models;

public class TransactionRecord
{
    private static readonly string[] DefaultMatchingFields = ["BranchCode", "FundCode", "TransactionNumber"];

    public string BranchCode { get; set; } = string.Empty;
    public string FundCode { get; set; } = string.Empty;
    public string TransactionNumber { get; set; } = string.Empty;
    public DateOnly TransactionDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public Dictionary<string, string> ExtraFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string MatchingKey => $"{BranchCode}|{FundCode}|{TransactionNumber}";

    public string CreateMatchingKey(IReadOnlyCollection<string> fields)
    {
        var effectiveFields = fields.Count == 0
            ? DefaultMatchingFields
            : fields;

        return string.Join("|", effectiveFields.Select(GetFieldValue));
    }

    public string GetFieldValue(string field)
    {
        return field switch
        {
            "BranchCode" => BranchCode,
            "FundCode" => FundCode,
            "TransactionNumber" => TransactionNumber,
            "TransactionDate" => TransactionDate.ToString("yyyy-MM-dd"),
            "Quantity" => Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Amount" => Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ when ExtraFields.TryGetValue(field, out var value) => value,
            _ => throw new InvalidOperationException($"Unsupported transaction field: {field}.")
        };
    }
}
