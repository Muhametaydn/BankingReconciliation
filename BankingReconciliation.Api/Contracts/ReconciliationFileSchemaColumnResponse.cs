namespace BankingReconciliation.Api.Contracts;

public class ReconciliationFileSchemaColumnResponse
{
    public int Position { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? DateFormat { get; set; }
    public string? Pattern { get; set; }
    public string? PatternDescription { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public int? MaxDecimalPlaces { get; set; }
    public int? FixedWidthStart { get; set; }
    public int? FixedWidthLength { get; set; }
    public string[] AllowedValues { get; set; } = [];
    public string Description { get; set; } = string.Empty;
}
