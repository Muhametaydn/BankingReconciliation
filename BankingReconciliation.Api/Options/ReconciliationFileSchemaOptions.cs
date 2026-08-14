namespace BankingReconciliation.Api.Options;

public class ReconciliationFileSchemaOptions
{
    public const string SectionName = "ReconciliationFileSchema";

    public ReconciliationFileSchemaColumnOptions[] Columns { get; set; } = [];

    public ReconciliationFileSchemaColumnOptions[] GetEffectiveColumns()
    {
        return Columns is null || Columns.Length == 0
            ? GetDefaultColumns()
            : Columns;
    }

    public static ReconciliationFileSchemaColumnOptions[] GetDefaultColumns()
    {
        return
        [
            new()
            {
                Field = "BranchCode",
                Name = "BranchCode",
                Type = "Text",
                Required = true,
                Description = "Sube/kaynak kodu. Matching key parcasidir."
            },
            new()
            {
                Field = "FundCode",
                Name = "FundCode",
                Type = "Text",
                Required = true,
                Description = "Fon kodu. Matching key parcasidir."
            },
            new()
            {
                Field = "TransactionNumber",
                Name = "TransactionNumber",
                Type = "Text",
                Required = true,
                Pattern = "^[A-Za-z0-9-]+$",
                PatternDescription = "Harf, rakam ve tire icerebilir.",
                Description = "Islem numarasi. Matching key parcasidir."
            },
            new()
            {
                Field = "TransactionDate",
                Name = "TransactionDate",
                Type = "Date",
                Required = true,
                DateFormat = "yyyy-MM-dd",
                Description = "Islem tarihi. yyyy-MM-dd formatinda olmalidir."
            },
            new()
            {
                Field = "Quantity",
                Name = "Quantity",
                Type = "Decimal",
                Required = true,
                Description = "Adet. Decimal sayi olmalidir."
            },
            new()
            {
                Field = "Amount",
                Name = "Amount",
                Type = "Decimal",
                Required = true,
                Description = "Tutar. Decimal sayi olmalidir."
            }
        ];
    }
}

public class ReconciliationFileSchemaColumnOptions
{
    public string Field { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
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
