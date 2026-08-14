using System.Text;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class CsvTransactionFileParserTests
{
    private readonly CsvTransactionFileParser _parser = new();

    [Fact]
    public async Task ParseAsync_ReturnsRecords_WhenCsvIsValid()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,2026-06-26,100.50,10000.25",
            "KADIKOY,B,TX002,2026-06-27,45,5000");

        var records = await _parser.ParseAsync(file);

        Assert.Equal(2, records.Count);
        Assert.Collection(
            records,
            first =>
            {
                Assert.Equal("BEYLIKDUZU", first.BranchCode);
                Assert.Equal("A", first.FundCode);
                Assert.Equal("TX001", first.TransactionNumber);
                Assert.Equal(new DateOnly(2026, 6, 26), first.TransactionDate);
                Assert.Equal(100.50m, first.Quantity);
                Assert.Equal(10000.25m, first.Amount);
            },
            second =>
            {
                Assert.Equal("KADIKOY", second.BranchCode);
                Assert.Equal("B", second.FundCode);
                Assert.Equal("TX002", second.TransactionNumber);
                Assert.Equal(new DateOnly(2026, 6, 27), second.TransactionDate);
                Assert.Equal(45m, second.Quantity);
                Assert.Equal(5000m, second.Amount);
            });
    }

    [Theory]
    [InlineData("|")]
    [InlineData("\t")]
    public async Task ParseAsync_ReturnsRecords_WhenTxtUsesSupportedDelimiter(string delimiter)
    {
        var file = CreateNamedFormFile(
            fileName: "transactions.txt",
            contentType: "text/plain",
            string.Join(delimiter, "BranchCode", "FundCode", "TransactionNumber", "TransactionDate", "Quantity", "Amount"),
            string.Join(delimiter, "BEYLIKDUZU", "A", "TX001", "2026-06-26", "100", "10000"));

        var record = Assert.Single(await _parser.ParseAsync(file));

        Assert.Equal("BEYLIKDUZU", record.BranchCode);
        Assert.Equal("A", record.FundCode);
        Assert.Equal("TX001", record.TransactionNumber);
        Assert.Equal(new DateOnly(2026, 6, 26), record.TransactionDate);
        Assert.Equal(100m, record.Quantity);
        Assert.Equal(10000m, record.Amount);
    }

    [Fact]
    public async Task ParseAsync_TrimsTextColumns_WhenValuesContainWhitespace()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            " BEYLIKDUZU , A , TX001 , 2026-06-26 , 100 , 10000 ");

        var record = Assert.Single(await _parser.ParseAsync(file));

        Assert.Equal("BEYLIKDUZU", record.BranchCode);
        Assert.Equal("A", record.FundCode);
        Assert.Equal("TX001", record.TransactionNumber);
        Assert.Equal("BEYLIKDUZU|A|TX001", record.MatchingKey);
    }

    [Fact]
    public async Task ParseAsync_PreservesTextWhitespace_WhenTrimTextValuesIsDisabled()
    {
        var parser = new CsvTransactionFileParser(Options.Create(new ReconciliationComparisonOptions
        {
            TrimTextValues = false
        }));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            " BEYLIKDUZU , A , TX001 , 2026-06-26 , 100 , 10000 ");

        var record = Assert.Single(await parser.ParseAsync(file));

        Assert.Equal(" BEYLIKDUZU ", record.BranchCode);
        Assert.Equal(" A ", record.FundCode);
        Assert.Equal(" TX001 ", record.TransactionNumber);
        Assert.Equal(" BEYLIKDUZU | A | TX001 ", record.MatchingKey);
    }

    [Fact]
    public async Task ParseAsync_UsesFieldSpecificTrimOption_WhenConfigured()
    {
        var parser = new CsvTransactionFileParser(Options.Create(new ReconciliationComparisonOptions
        {
            TrimTextValues = false,
            TrimTransactionNumber = true
        }));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            " BEYLIKDUZU , A , TX001 , 2026-06-26 , 100 , 10000 ");

        var record = Assert.Single(await parser.ParseAsync(file));

        Assert.Equal(" BEYLIKDUZU ", record.BranchCode);
        Assert.Equal(" A ", record.FundCode);
        Assert.Equal("TX001", record.TransactionNumber);
        Assert.Equal(" BEYLIKDUZU | A |TX001", record.MatchingKey);
    }

    [Fact]
    public async Task ParseAsync_NormalizesCodes_WhenComparisonOptionsContainMappings()
    {
        var parser = new CsvTransactionFileParser(Options.Create(new ReconciliationComparisonOptions
        {
            BranchCodeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["beylikduzu sube"] = "BEYLIKDUZU"
            },
            FundCodeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["a fonu"] = "A"
            }
        }));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            " beylikduzu sube , a fonu , TX001 , 2026-06-26 , 100 , 10000 ");

        var record = Assert.Single(await parser.ParseAsync(file));

        Assert.Equal("BEYLIKDUZU", record.BranchCode);
        Assert.Equal("A", record.FundCode);
        Assert.Equal("BEYLIKDUZU|A|TX001", record.MatchingKey);
    }

    [Fact]
    public async Task ParseAsync_NormalizesTransactionNumber_WhenComparisonOptionsContainMapping()
    {
        var parser = new CsvTransactionFileParser(Options.Create(new ReconciliationComparisonOptions
        {
            TransactionNumberMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tx-001"] = "TX001"
            }
        }));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,tx-001,2026-06-26,100,10000");

        var record = Assert.Single(await parser.ParseAsync(file));

        Assert.Equal("TX001", record.TransactionNumber);
        Assert.Equal("BEYLIKDUZU|A|TX001", record.MatchingKey);
    }

    [Fact]
    public async Task ParseAsync_NormalizesCodes_WhenComparisonOptionsContainGenericFieldMappings()
    {
        var parser = new CsvTransactionFileParser(Options.Create(new ReconciliationComparisonOptions
        {
            FieldMappings = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["BranchCode"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["beylikduzu sube"] = "BEYLIKDUZU"
                },
                ["FundCode"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["a fonu"] = "A"
                },
                ["TransactionNumber"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["tx-001"] = "TX001"
                }
            }
        }));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            " beylikduzu sube , a fonu , tx-001 , 2026-06-26 , 100 , 10000 ");

        var record = Assert.Single(await parser.ParseAsync(file));

        Assert.Equal("BEYLIKDUZU", record.BranchCode);
        Assert.Equal("A", record.FundCode);
        Assert.Equal("TX001", record.TransactionNumber);
        Assert.Equal("BEYLIKDUZU|A|TX001", record.MatchingKey);
    }

    [Fact]
    public async Task ParseAsync_PrefersSpecificMappings_WhenGenericFieldMappingsAlsoContainValue()
    {
        var parser = new CsvTransactionFileParser(Options.Create(new ReconciliationComparisonOptions
        {
            BranchCodeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BEYLIKDUZU SUBE"] = "BEYLIKDUZU"
            },
            FieldMappings = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["BranchCode"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["BEYLIKDUZU SUBE"] = "GENERIC"
                }
            }
        }));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU SUBE,A,TX001,2026-06-26,100,10000");

        var record = Assert.Single(await parser.ParseAsync(file));

        Assert.Equal("BEYLIKDUZU", record.BranchCode);
        Assert.Equal("BEYLIKDUZU|A|TX001", record.MatchingKey);
    }

    [Fact]
    public async Task ParseAsync_UsesConfiguredSchemaOrderAndHeaderNames()
    {
        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(new ReconciliationFileSchemaOptions
            {
                Columns =
                [
                    new()
                    {
                        Field = "TransactionNumber",
                        Name = "IslemNo",
                        Type = "Text",
                        Required = true
                    },
                    new()
                    {
                        Field = "BranchCode",
                        Name = "SubeKodu",
                        Type = "Text",
                        Required = true
                    },
                    new()
                    {
                        Field = "FundCode",
                        Name = "FonKodu",
                        Type = "Text",
                        Required = true
                    },
                    new()
                    {
                        Field = "TransactionDate",
                        Name = "Tarih",
                        Type = "Date",
                        Required = true,
                        DateFormat = "yyyy-MM-dd"
                    },
                    new()
                    {
                        Field = "Amount",
                        Name = "Tutar",
                        Type = "Decimal",
                        Required = true
                    },
                    new()
                    {
                        Field = "Quantity",
                        Name = "Adet",
                        Type = "Decimal",
                        Required = true
                    }
                ]
            }));
        var file = CreateFormFile(
            "IslemNo,SubeKodu,FonKodu,Tarih,Tutar,Adet",
            "TX001,BEYLIKDUZU,A,2026-06-26,10000,100");

        var record = Assert.Single(await parser.ParseAsync(file));

        Assert.Equal("BEYLIKDUZU", record.BranchCode);
        Assert.Equal("A", record.FundCode);
        Assert.Equal("TX001", record.TransactionNumber);
        Assert.Equal(new DateOnly(2026, 6, 26), record.TransactionDate);
        Assert.Equal(100m, record.Quantity);
        Assert.Equal(10000m, record.Amount);
    }

    [Fact]
    public async Task ParseAsync_StoresExtraSchemaColumns()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns =
            [
                .. ReconciliationFileSchemaOptions.GetDefaultColumns(),
                new()
                {
                    Field = "Commission",
                    Name = "Commission",
                    Type = "Decimal",
                    Required = false,
                    MaxDecimalPlaces = 2
                }
            ]
        };
        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(options));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount,Commission",
            "BEYLIKDUZU,A,TX001,2026-06-26,100,10000,12.34");

        var record = Assert.Single(await parser.ParseAsync(file));

        Assert.Equal("12.34", record.ExtraFields["Commission"]);
        Assert.Equal("12.34", record.GetFieldValue("Commission"));
    }

    [Fact]
    public async Task ParseAsync_ValidatesExtraSchemaColumns()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns =
            [
                .. ReconciliationFileSchemaOptions.GetDefaultColumns(),
                new()
                {
                    Field = "Commission",
                    Name = "Commission",
                    Type = "Decimal",
                    Required = false,
                    MaxDecimalPlaces = 2
                }
            ]
        };
        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(options));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount,Commission",
            "BEYLIKDUZU,A,TX001,2026-06-26,100,10000,12.345");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("Commission", exception.ColumnName);
        Assert.Contains("2 decimal places or fewer", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_UsesConfiguredIntegerSchemaRule()
    {
        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(CreateSchemaWithIntegerTransactionNumber()));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,2026-06-26,100,10000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("TransactionNumber", exception.ColumnName);
        Assert.Contains("TransactionNumber must be a valid integer number", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_ReturnsRecord_WhenConfiguredIntegerSchemaRulePasses()
    {
        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(CreateSchemaWithIntegerTransactionNumber()));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,1001,2026-06-26,100,10000");

        var record = Assert.Single(await parser.ParseAsync(file));

        Assert.Equal("1001", record.TransactionNumber);
        Assert.Equal("BEYLIKDUZU|A|1001", record.MatchingKey);
    }

    [Fact]
    public async Task ParseAsync_UsesConfiguredPatternRule()
    {
        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(CreateSchemaWithTransactionNumberPattern()));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX 001,2026-06-26,100,10000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("TransactionNumber", exception.ColumnName);
        Assert.Contains("TransactionNumber must match rule: harf ve rakam icermelidir", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_UsesConfiguredLengthRules()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        var transactionNumberColumn = Assert.Single(
            options.Columns,
            column => column.Field == "TransactionNumber");
        transactionNumberColumn.MaxLength = 5;

        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(options));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX0019,2026-06-26,100,10000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("TransactionNumber", exception.ColumnName);
        Assert.Contains("TransactionNumber must be 5 characters or fewer", exception.Message);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsConfiguredLengthRuleErrors()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        var branchCodeColumn = Assert.Single(
            options.Columns,
            column => column.Field == "BranchCode");
        branchCodeColumn.MinLength = 3;

        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(options));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BE,A,TX001,2026-06-26,100,10000");

        var result = await parser.ValidateAsync(file);
        var error = Assert.Single(result.Errors);

        Assert.False(result.IsValid);
        Assert.Equal(2, error.RowNumber);
        Assert.Equal("BranchCode", error.ColumnName);
        Assert.Contains("BranchCode must be at least 3 characters", error.Message);
    }

    [Fact]
    public async Task ParseAsync_UsesConfiguredAllowedValuesRule()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        var fundCodeColumn = Assert.Single(
            options.Columns,
            column => column.Field == "FundCode");
        fundCodeColumn.AllowedValues = ["A", "B"];

        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(options));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,C,TX001,2026-06-26,100,10000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("FundCode", exception.ColumnName);
        Assert.Contains("FundCode must be one of these values: A, B", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_UsesConfiguredNumericRangeRule()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        var quantityColumn = Assert.Single(
            options.Columns,
            column => column.Field == "Quantity");
        quantityColumn.MinValue = 1;
        quantityColumn.MaxValue = 500;

        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(options));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,2026-06-26,0,10000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("Quantity", exception.ColumnName);
        Assert.Contains("Quantity must be greater than or equal to 1", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_UsesConfiguredMaxDecimalPlacesRule()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        var amountColumn = Assert.Single(
            options.Columns,
            column => column.Field == "Amount");
        amountColumn.MaxDecimalPlaces = 2;

        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(options));
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,2026-06-26,100,10000.123");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("Amount", exception.ColumnName);
        Assert.Contains("Amount must have 2 decimal places or fewer", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_HandlesQuotedColumns_WhenValueContainsComma()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "\"BEYLIKDUZU, ISTANBUL\",A,TX001,2026-06-26,100,10000");

        var record = Assert.Single(await _parser.ParseAsync(file));

        Assert.Equal("BEYLIKDUZU, ISTANBUL", record.BranchCode);
        Assert.Equal("BEYLIKDUZU, ISTANBUL|A|TX001", record.MatchingKey);
    }

    [Fact]
    public async Task ParseAsync_ReturnsEmptyList_WhenFileHasNoHeader()
    {
        var file = CreateFormFile();

        var records = await _parser.ParseAsync(file);

        Assert.Empty(records);
    }

    [Fact]
    public async Task ParseAsync_ThrowsCsvTransactionFileParseException_WhenHeaderIsInvalid()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,100,10000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            _parser.ParseAsync(file));

        Assert.Equal(1, exception.RowNumber);
        Assert.Equal("Header", exception.ColumnName);
        Assert.Contains("Delimited file header must be", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_ThrowsCsvTransactionFileParseException_WhenColumnCountIsInvalid()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,2026-06-26,100");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            _parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("Row", exception.ColumnName);
        Assert.Contains("must contain 6 columns", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_ThrowsCsvTransactionFileParseException_WhenRequiredTextColumnIsEmpty()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,,TX001,2026-06-26,100,10000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            _parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("FundCode", exception.ColumnName);
        Assert.Contains("FundCode is required", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_ThrowsCsvTransactionFileParseException_WhenDateFormatIsInvalid()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,26.06.2026,100,10000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            _parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("TransactionDate", exception.ColumnName);
        Assert.Contains("TransactionDate must use yyyy-MM-dd format", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_ThrowsCsvTransactionFileParseException_WhenDecimalValueIsInvalid()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,2026-06-26,not-a-number,10000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            _parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("Quantity", exception.ColumnName);
        Assert.Contains("Quantity must be a valid decimal number", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_ThrowsCsvTransactionFileParseException_WhenAmountValueIsInvalid()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,2026-06-26,100,not-a-number");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            _parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("Amount", exception.ColumnName);
        Assert.Contains("Amount must be a valid decimal number", exception.Message);
    }

    [Fact]
    public async Task ParseAsync_ThrowsCsvTransactionFileParseException_WhenQuotedColumnIsNotClosed()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "\"BEYLIKDUZU,A,TX001,2026-06-26,100,10000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            _parser.ParseAsync(file));

        Assert.Equal(2, exception.RowNumber);
        Assert.Equal("Row", exception.ColumnName);
        Assert.Contains("unclosed quoted column", exception.Message);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsAllColumnErrors_AcrossRows()
    {
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,invalid-date,invalid-quantity,invalid-amount",
            ",B,TX002,2026-06-27,invalid-quantity,5000");

        var result = await _parser.ValidateAsync(file);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.RecordCount);
        Assert.Collection(
            result.Errors,
            error => AssertError(error, 2, "TransactionDate"),
            error => AssertError(error, 2, "Quantity"),
            error => AssertError(error, 2, "Amount"),
            error => AssertError(error, 3, "BranchCode"),
            error => AssertError(error, 3, "Quantity"));
    }

    [Fact]
    public async Task ParseAsync_ReturnsRecords_WhenTxtUsesFixedWidthSchema()
    {
        var schema = CreateFixedWidthSchema();
        var parser = new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(schema));
        var header = CreateFixedWidthLine(schema, schema.Columns.Select(column => column.Name).ToArray());
        var row = CreateFixedWidthLine(
            schema,
            ["BEYLIKDUZU", "A", "TX001", "2026-06-26", "100.50", "10000.25"]);
        var file = CreateNamedFormFile("transactions.txt", "text/plain", header, row);

        var records = await parser.ParseAsync(file);

        var record = Assert.Single(records);
        Assert.Equal("BEYLIKDUZU", record.BranchCode);
        Assert.Equal("A", record.FundCode);
        Assert.Equal("TX001", record.TransactionNumber);
        Assert.Equal(new DateOnly(2026, 6, 26), record.TransactionDate);
        Assert.Equal(100.50m, record.Quantity);
        Assert.Equal(10000.25m, record.Amount);
    }

    [Fact]
    public async Task ParseAsync_ThrowsClearError_WhenRecordLimitIsExceeded()
    {
        var parser = CreateParserWithRecordLimit(1);
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,2026-06-26,100.50,10000.25",
            "KADIKOY,B,TX002,2026-06-27,45,5000");

        var exception = await Assert.ThrowsAsync<CsvTransactionFileParseException>(() =>
            parser.ParseAsync(file));

        Assert.Equal(3, exception.RowNumber);
        Assert.Equal("Row", exception.ColumnName);
        Assert.Contains("maximum of 1 records", exception.Message);
    }

    [Fact]
    public async Task ValidateAsync_StopsAtRecordLimitAndReturnsClearError()
    {
        var parser = CreateParserWithRecordLimit(1);
        var file = CreateFormFile(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount",
            "BEYLIKDUZU,A,TX001,2026-06-26,100.50,10000.25",
            "KADIKOY,B,TX002,2026-06-27,45,5000",
            "BESIKTAS,C,TX003,2026-06-28,20,2500");

        var result = await parser.ValidateAsync(file);

        Assert.Equal(2, result.RecordCount);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.RowNumber);
        Assert.Contains("maximum of 1 records", error.Message);
    }

    private static CsvTransactionFileParser CreateParserWithRecordLimit(int maxRecords)
    {
        return new CsvTransactionFileParser(
            Options.Create(new ReconciliationComparisonOptions()),
            Options.Create(new ReconciliationFileSchemaOptions()),
            Options.Create(new ReconciliationUploadOptions
            {
                MaxRecordsPerFile = maxRecords
            }));
    }

    private static ReconciliationFileSchemaOptions CreateFixedWidthSchema()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        var lengths = new[] { 14, 10, 20, 15, 14, 16 };
        var start = 1;

        for (var index = 0; index < options.Columns.Length; index++)
        {
            options.Columns[index].FixedWidthStart = start;
            options.Columns[index].FixedWidthLength = lengths[index];
            start += lengths[index];
        }

        return options;
    }

    private static string CreateFixedWidthLine(
        ReconciliationFileSchemaOptions schema,
        IReadOnlyList<string> values)
    {
        return string.Concat(schema.Columns.Select((column, index) =>
            values[index].PadRight(column.FixedWidthLength!.Value)));
    }

    private static IFormFile CreateFormFile(params string[] lines)
    {
        return CreateNamedFormFile("transactions.csv", "text/csv", lines);
    }

    private static void AssertError(
        CsvTransactionFileParseException error,
        int rowNumber,
        string columnName)
    {
        Assert.Equal(rowNumber, error.RowNumber);
        Assert.Equal(columnName, error.ColumnName);
    }

    private static ReconciliationFileSchemaOptions CreateSchemaWithIntegerTransactionNumber()
    {
        return new ReconciliationFileSchemaOptions
        {
            Columns =
            [
                new()
                {
                    Field = "BranchCode",
                    Name = "BranchCode",
                    Type = "Text",
                    Required = true
                },
                new()
                {
                    Field = "FundCode",
                    Name = "FundCode",
                    Type = "Text",
                    Required = true
                },
                new()
                {
                    Field = "TransactionNumber",
                    Name = "TransactionNumber",
                    Type = "Integer",
                    Required = true
                },
                new()
                {
                    Field = "TransactionDate",
                    Name = "TransactionDate",
                    Type = "Date",
                    Required = true,
                    DateFormat = "yyyy-MM-dd"
                },
                new()
                {
                    Field = "Quantity",
                    Name = "Quantity",
                    Type = "Decimal",
                    Required = true
                },
                new()
                {
                    Field = "Amount",
                    Name = "Amount",
                    Type = "Decimal",
                    Required = true
                }
            ]
        };
    }

    private static ReconciliationFileSchemaOptions CreateSchemaWithTransactionNumberPattern()
    {
        var options = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        var transactionNumberColumn = Assert.Single(
            options.Columns,
            column => column.Field == "TransactionNumber");
        transactionNumberColumn.Pattern = "^[A-Z0-9]+$";
        transactionNumberColumn.PatternDescription = "harf ve rakam icermelidir";

        return options;
    }

    private static IFormFile CreateNamedFormFile(
        string fileName,
        string contentType,
        params string[] lines)
    {
        var content = string.Join(Environment.NewLine, lines);
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);

        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
