namespace BankingReconciliation.Api.Options;

public class ReconciliationComparisonOptions
{
    public const string SectionName = "ReconciliationComparison";

    public bool NormalizeCodeCase { get; set; } = true;
    public bool TrimTextValues { get; set; } = true;
    public bool? TrimBranchCode { get; set; }
    public bool? TrimFundCode { get; set; }
    public bool? TrimTransactionNumber { get; set; }
    public bool RequireExactMatch { get; set; }
    public decimal QuantityTolerance { get; set; }
    public decimal AmountTolerance { get; set; }
    public int? QuantityDecimalPlaces { get; set; }
    public int? BranchQuantityDecimalPlaces { get; set; }
    public int? BankQuantityDecimalPlaces { get; set; }
    public int? AmountDecimalPlaces { get; set; }
    public int? BranchAmountDecimalPlaces { get; set; }
    public int? BankAmountDecimalPlaces { get; set; }
    public string[] MatchingFields { get; set; } = [];
    public string[] ComparisonFields { get; set; } = [];
    public string[] ResultFields { get; set; } = [];
    public Dictionary<string, string> BranchCodeMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FundCodeMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> TransactionNumberMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> FieldMappings { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
