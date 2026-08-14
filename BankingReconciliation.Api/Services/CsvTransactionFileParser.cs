using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BankingReconciliation.Api.Contracts;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public class CsvTransactionFileParser : ITransactionFileParser
{
    private readonly ReconciliationComparisonOptions _comparisonOptions;
    private readonly int _maxRecordsPerFile;
    private readonly TransactionColumnDefinition[] _columnDefinitions;
    private readonly string[] _expectedHeader;
    private readonly bool _usesFixedWidth;
    private static readonly char[] SupportedDelimiters = [',', '|', '\t'];

    public CsvTransactionFileParser()
        : this(
            Microsoft.Extensions.Options.Options.Create(new ReconciliationComparisonOptions()),
            Microsoft.Extensions.Options.Options.Create(new ReconciliationFileSchemaOptions()),
            Microsoft.Extensions.Options.Options.Create(new ReconciliationUploadOptions()))
    {
    }

    public CsvTransactionFileParser(IOptions<ReconciliationComparisonOptions> comparisonOptions)
        : this(
            comparisonOptions,
            Microsoft.Extensions.Options.Options.Create(new ReconciliationFileSchemaOptions()),
            Microsoft.Extensions.Options.Options.Create(new ReconciliationUploadOptions()))
    {
    }

    public CsvTransactionFileParser(
        IOptions<ReconciliationComparisonOptions> comparisonOptions,
        IOptions<ReconciliationFileSchemaOptions> fileSchemaOptions)
        : this(
            comparisonOptions,
            fileSchemaOptions,
            Microsoft.Extensions.Options.Options.Create(new ReconciliationUploadOptions()))
    {
    }

    public CsvTransactionFileParser(
        IOptions<ReconciliationComparisonOptions> comparisonOptions,
        IOptions<ReconciliationFileSchemaOptions> fileSchemaOptions,
        IOptions<ReconciliationUploadOptions> uploadOptions)
    {
        _comparisonOptions = comparisonOptions.Value;
        _maxRecordsPerFile = uploadOptions.Value.MaxRecordsPerFile;
        _columnDefinitions = CreateColumnDefinitions(fileSchemaOptions.Value);
        _expectedHeader = _columnDefinitions
            .Select(column => column.Name)
            .ToArray();
        _usesFixedWidth = _columnDefinitions.All(column =>
            column.FixedWidthStart is not null && column.FixedWidthLength is not null);
    }

    public static IReadOnlyCollection<ReconciliationFileSchemaColumnResponse> GetSchema(
        ReconciliationFileSchemaOptions fileSchemaOptions)
    {
        return CreateColumnDefinitions(fileSchemaOptions)
            .Select((column, index) => new ReconciliationFileSchemaColumnResponse
            {
                Position = index + 1,
                Field = column.Field,
                Name = column.Name,
                Type = column.Type.ToString(),
                Required = column.Required,
                DateFormat = column.DateFormat,
                Pattern = column.Pattern,
                PatternDescription = column.PatternDescription,
                MinLength = column.MinLength,
                MaxLength = column.MaxLength,
                MinValue = column.MinValue,
                MaxValue = column.MaxValue,
                MaxDecimalPlaces = column.MaxDecimalPlaces,
                FixedWidthStart = column.FixedWidthStart,
                FixedWidthLength = column.FixedWidthLength,
                AllowedValues = column.AllowedValues ?? [],
                Description = column.Description
            })
            .ToList();
    }

    public async Task<List<TransactionRecord>> ParseAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        await using var stream = file.OpenReadStream();
        return await ParseAsync(stream, cancellationToken);
    }

    public async Task<List<TransactionRecord>> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var records = new List<TransactionRecord>();
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);

        var header = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(header))
        {
            return records;
        }

        var delimiter = _usesFixedWidth ? (char?)null : DetectDelimiter(header);
        ValidateHeader(header, delimiter);

        var rowNumber = 1;
        while (!reader.EndOfStream)
        {
            rowNumber++;
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (records.Count >= _maxRecordsPerFile)
            {
                throw CreateRecordLimitError(rowNumber);
            }

            var columns = ParseColumns(line, delimiter, rowNumber);
            ValidateColumnCount(columns, rowNumber);
            ValidateColumnValues(columns, rowNumber);
            var columnValues = CreateColumnValueLookup(columns);

            records.Add(new TransactionRecord
            {
                BranchCode = NormalizeMappedText(
                    "BranchCode",
                    RequiredText(columnValues["BranchCode"], "BranchCode", rowNumber, ShouldTrimBranchCode()),
                    _comparisonOptions.BranchCodeMappings,
                    ShouldTrimBranchCode()),
                FundCode = NormalizeMappedText(
                    "FundCode",
                    RequiredText(columnValues["FundCode"], "FundCode", rowNumber, ShouldTrimFundCode()),
                    _comparisonOptions.FundCodeMappings,
                    ShouldTrimFundCode()),
                TransactionNumber = NormalizeMappedText(
                    "TransactionNumber",
                    RequiredText(
                        columnValues["TransactionNumber"],
                        "TransactionNumber",
                        rowNumber,
                        ShouldTrimTransactionNumber()),
                    _comparisonOptions.TransactionNumberMappings,
                    ShouldTrimTransactionNumber()),
                TransactionDate = ParseDate(
                    columnValues["TransactionDate"],
                    "TransactionDate",
                    rowNumber,
                    _columnDefinitions.Single(column => column.Field == "TransactionDate").DateFormat!),
                Quantity = ParseDecimal(columnValues["Quantity"], "Quantity", rowNumber),
                Amount = ParseDecimal(columnValues["Amount"], "Amount", rowNumber),
                ExtraFields = CreateExtraFieldValueLookup(columnValues)
            });
        }

        return records;
    }

    public async Task<TransactionFileValidationResult> ValidateAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<CsvTransactionFileParseException>();
        var recordCount = 0;

        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);

        var header = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(header))
        {
            return new TransactionFileValidationResult();
        }

        char? delimiter;
        try
        {
            delimiter = _usesFixedWidth ? null : DetectDelimiter(header);
            ValidateHeader(header, delimiter);
        }
        catch (CsvTransactionFileParseException exception)
        {
            return new TransactionFileValidationResult
            {
                Errors = [exception]
            };
        }

        var rowNumber = 1;
        while (!reader.EndOfStream)
        {
            rowNumber++;
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            recordCount++;
            if (recordCount > _maxRecordsPerFile)
            {
                errors.Add(CreateRecordLimitError(rowNumber));
                break;
            }

            string[] columns;
            try
            {
                columns = ParseColumns(line, delimiter, rowNumber);
                ValidateColumnCount(columns, rowNumber);
            }
            catch (CsvTransactionFileParseException exception)
            {
                errors.Add(exception);
                continue;
            }

            errors.AddRange(GetColumnValidationErrors(columns, rowNumber));
        }

        return new TransactionFileValidationResult
        {
            RecordCount = recordCount,
            Errors = errors
        };
    }

    private CsvTransactionFileParseException CreateRecordLimitError(int rowNumber)
    {
        return new CsvTransactionFileParseException(
            rowNumber,
            $"File exceeds the maximum of {_maxRecordsPerFile} records.",
            "Row");
    }

    private static TransactionColumnDefinition[] CreateColumnDefinitions(
        ReconciliationFileSchemaOptions fileSchemaOptions)
    {
        return fileSchemaOptions.GetEffectiveColumns()
            .Select(column => new TransactionColumnDefinition(
                column.Field,
                column.Name,
                Enum.Parse<TransactionColumnType>(column.Type, ignoreCase: true),
                column.Required,
                column.DateFormat,
                column.Pattern,
                column.PatternDescription,
                column.MinLength,
                column.MaxLength,
                column.MinValue,
                column.MaxValue,
                column.MaxDecimalPlaces,
                column.FixedWidthStart,
                column.FixedWidthLength,
                column.AllowedValues,
                column.Description))
            .ToArray();
    }

    private Dictionary<string, string> CreateColumnValueLookup(string[] columns)
    {
        return _columnDefinitions
            .Select((definition, index) => new
            {
                definition.Field,
                Value = columns[index]
            })
            .ToDictionary(column => column.Field, column => column.Value);
    }

    private static Dictionary<string, string> CreateExtraFieldValueLookup(
        IReadOnlyDictionary<string, string> columnValues)
    {
        var coreFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BranchCode",
            "FundCode",
            "TransactionNumber",
            "TransactionDate",
            "Quantity",
            "Amount"
        };

        return columnValues
            .Where(column => !coreFields.Contains(column.Key))
            .ToDictionary(
                column => column.Key,
                column => column.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private bool ShouldTrimBranchCode()
    {
        return _comparisonOptions.TrimBranchCode ?? _comparisonOptions.TrimTextValues;
    }

    private bool ShouldTrimFundCode()
    {
        return _comparisonOptions.TrimFundCode ?? _comparisonOptions.TrimTextValues;
    }

    private bool ShouldTrimTransactionNumber()
    {
        return _comparisonOptions.TrimTransactionNumber ?? _comparisonOptions.TrimTextValues;
    }

    private string NormalizeMappedText(
        string field,
        string value,
        IReadOnlyDictionary<string, string> mappings,
        bool trimValue)
    {
        var normalizedValue = NormalizeText(value, trimValue);

        if (TryGetMappingValue(mappings, normalizedValue, out var mappedValue) ||
            TryGetMappingValue(GetFieldMappings(field), normalizedValue, out mappedValue))
        {
            normalizedValue = NormalizeText(mappedValue, trimValue);
        }

        return _comparisonOptions.NormalizeCodeCase
            ? normalizedValue.ToUpperInvariant()
            : normalizedValue;
    }

    private IReadOnlyDictionary<string, string> GetFieldMappings(string field)
    {
        var fieldMappings = _comparisonOptions.FieldMappings
            .FirstOrDefault(mapping =>
                string.Equals(mapping.Key, field, StringComparison.OrdinalIgnoreCase));

        return fieldMappings.Value ?? new Dictionary<string, string>();
    }

    private static bool TryGetMappingValue(
        IReadOnlyDictionary<string, string> mappings,
        string key,
        out string mappedValue)
    {
        if (mappings.TryGetValue(key, out mappedValue!))
        {
            return true;
        }

        foreach (var mapping in mappings)
        {
            if (string.Equals(mapping.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                mappedValue = mapping.Value;
                return true;
            }
        }

        mappedValue = string.Empty;
        return false;
    }

    private static string NormalizeText(string value, bool trimValue)
    {
        return trimValue
            ? value.Trim()
            : value;
    }

    private void ValidateHeader(string header, char? delimiter)
    {
        var columns = ParseColumns(header, delimiter, rowNumber: 1)
            .Select(column => column.Trim())
            .ToArray();

        if (!_expectedHeader.SequenceEqual(columns, StringComparer.OrdinalIgnoreCase))
        {
            var message = _usesFixedWidth
                ? $"Fixed-width file header must contain: {string.Join(", ", _expectedHeader)}."
                : $"Delimited file header must be: {string.Join(delimiter!.Value, _expectedHeader)}.";
            throw new CsvTransactionFileParseException(
                rowNumber: 1,
                message,
                columnName: "Header");
        }
    }

    private string[] ParseColumns(string line, char? delimiter, int rowNumber)
    {
        return _usesFixedWidth
            ? ParseFixedWidthLine(line, rowNumber)
            : ParseDelimitedLine(line, delimiter!.Value, rowNumber);
    }

    private string[] ParseFixedWidthLine(string line, int rowNumber)
    {
        var requiredLength = _columnDefinitions.Max(column =>
            column.FixedWidthStart!.Value - 1 + column.FixedWidthLength!.Value);
        if (line.Length < requiredLength)
        {
            throw new CsvTransactionFileParseException(
                rowNumber,
                $"Fixed-width row must contain at least {requiredLength} characters, but it contains {line.Length}.",
                "Row");
        }

        return _columnDefinitions
            .Select(column => line.Substring(
                    column.FixedWidthStart!.Value - 1,
                    column.FixedWidthLength!.Value)
                .Trim())
            .ToArray();
    }

    private char DetectDelimiter(string header)
    {
        return SupportedDelimiters
            .Select(delimiter => new
            {
                Delimiter = delimiter,
                Columns = ParseDelimitedLine(header, delimiter, rowNumber: 1)
                    .Select(column => column.Trim())
                    .ToArray()
            })
            .Where(candidate => candidate.Columns.Length == _expectedHeader.Length)
            .OrderByDescending(candidate => _expectedHeader.SequenceEqual(
                candidate.Columns,
                StringComparer.OrdinalIgnoreCase))
            .ThenByDescending(candidate => candidate.Columns.Count(column => !string.IsNullOrWhiteSpace(column)))
            .FirstOrDefault()
            ?.Delimiter ?? ',';
    }

    private void ValidateColumnCount(string[] columns, int rowNumber)
    {
        if (columns.Length != _expectedHeader.Length)
        {
            throw new CsvTransactionFileParseException(
                rowNumber,
                $"Delimited row must contain {_expectedHeader.Length} columns, but it contains {columns.Length}.",
                "Row");
        }
    }

    private void ValidateColumnValues(string[] columns, int rowNumber)
    {
        for (var index = 0; index < _columnDefinitions.Length; index++)
        {
            var definition = _columnDefinitions[index];
            var value = columns[index];

            if (definition.Required && string.IsNullOrWhiteSpace(value))
            {
                throw new CsvTransactionFileParseException(
                    rowNumber,
                    $"{definition.Name} is required.",
                    definition.Name);
            }

            ValidatePattern(value, definition, rowNumber);
            ValidateLength(value, definition, rowNumber);
            ValidateAllowedValues(value, definition, rowNumber);

            switch (definition.Type)
            {
                case TransactionColumnType.Text:
                    break;
                case TransactionColumnType.Date:
                    ParseDate(value, definition.Name, rowNumber, definition.DateFormat!);
                    break;
                case TransactionColumnType.Decimal:
                    ValidateDecimalPlaces(value, definition, rowNumber);
                    ValidateNumericRange(
                        ParseDecimal(value, definition.Name, rowNumber),
                        definition,
                        rowNumber);
                    break;
                case TransactionColumnType.Integer:
                    ValidateNumericRange(
                        ParseInteger(value, definition.Name, rowNumber),
                        definition,
                        rowNumber);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported transaction column type: {definition.Type}.");
            }
        }
    }

    private IReadOnlyList<CsvTransactionFileParseException> GetColumnValidationErrors(
        string[] columns,
        int rowNumber)
    {
        var errors = new List<CsvTransactionFileParseException>();

        for (var index = 0; index < _columnDefinitions.Length; index++)
        {
            var definition = _columnDefinitions[index];
            var value = columns[index];

            if (definition.Required && string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new CsvTransactionFileParseException(
                    rowNumber,
                    $"{definition.Name} is required.",
                    definition.Name));
                continue;
            }

            try
            {
                ValidatePattern(value, definition, rowNumber);
                ValidateLength(value, definition, rowNumber);
                ValidateAllowedValues(value, definition, rowNumber);

                switch (definition.Type)
                {
                    case TransactionColumnType.Text:
                        break;
                    case TransactionColumnType.Date:
                        ParseDate(value, definition.Name, rowNumber, definition.DateFormat!);
                        break;
                    case TransactionColumnType.Decimal:
                        ValidateDecimalPlaces(value, definition, rowNumber);
                        ValidateNumericRange(
                            ParseDecimal(value, definition.Name, rowNumber),
                            definition,
                            rowNumber);
                        break;
                    case TransactionColumnType.Integer:
                        ValidateNumericRange(
                            ParseInteger(value, definition.Name, rowNumber),
                            definition,
                            rowNumber);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported transaction column type: {definition.Type}.");
                }
            }
            catch (CsvTransactionFileParseException exception)
            {
                errors.Add(exception);
            }
        }

        return errors;
    }

    private static string RequiredText(
        string value,
        string columnName,
        int rowNumber,
        bool trimValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CsvTransactionFileParseException(rowNumber, $"{columnName} is required.", columnName);
        }

        return NormalizeText(value, trimValue);
    }

    private static DateOnly ParseDate(
        string value,
        string columnName,
        int rowNumber,
        string dateFormat)
    {
        if (!DateOnly.TryParseExact(
            value.Trim(),
            dateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            throw new CsvTransactionFileParseException(
                rowNumber,
                $"{columnName} must use {dateFormat} format.",
                columnName);
        }

        return date;
    }

    private static decimal ParseDecimal(string value, string columnName, int rowNumber)
    {
        if (!decimal.TryParse(
            value.Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var number))
        {
            throw new CsvTransactionFileParseException(
                rowNumber,
                $"{columnName} must be a valid decimal number.",
                columnName);
        }

        return number;
    }

    private static void ValidatePattern(
        string value,
        TransactionColumnDefinition definition,
        int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(definition.Pattern) ||
            string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Regex.IsMatch(value.Trim(), definition.Pattern))
        {
            var ruleDescription = string.IsNullOrWhiteSpace(definition.PatternDescription)
                ? $"match pattern {definition.Pattern}"
                : definition.PatternDescription;

            throw new CsvTransactionFileParseException(
                rowNumber,
                $"{definition.Name} must match rule: {ruleDescription}.",
                definition.Name);
        }
    }

    private static void ValidateLength(
        string value,
        TransactionColumnDefinition definition,
        int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var length = value.Trim().Length;

        if (definition.MinLength is not null && length < definition.MinLength)
        {
            throw new CsvTransactionFileParseException(
                rowNumber,
                $"{definition.Name} must be at least {definition.MinLength} characters.",
                definition.Name);
        }

        if (definition.MaxLength is not null && length > definition.MaxLength)
        {
            throw new CsvTransactionFileParseException(
                rowNumber,
                $"{definition.Name} must be {definition.MaxLength} characters or fewer.",
                definition.Name);
        }
    }

    private static void ValidateAllowedValues(
        string value,
        TransactionColumnDefinition definition,
        int rowNumber)
    {
        if (definition.AllowedValues is null ||
            definition.AllowedValues.Length == 0 ||
            string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalizedValue = value.Trim();
        if (definition.AllowedValues.Any(allowedValue =>
                string.Equals(allowedValue.Trim(), normalizedValue, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        throw new CsvTransactionFileParseException(
            rowNumber,
            $"{definition.Name} must be one of these values: {string.Join(", ", definition.AllowedValues)}.",
            definition.Name);
    }

    private static void ValidateNumericRange(
        decimal value,
        TransactionColumnDefinition definition,
        int rowNumber)
    {
        if (definition.MinValue is not null && value < definition.MinValue)
        {
            throw new CsvTransactionFileParseException(
                rowNumber,
                $"{definition.Name} must be greater than or equal to {definition.MinValue}.",
                definition.Name);
        }

        if (definition.MaxValue is not null && value > definition.MaxValue)
        {
            throw new CsvTransactionFileParseException(
                rowNumber,
                $"{definition.Name} must be less than or equal to {definition.MaxValue}.",
                definition.Name);
        }
    }

    private static void ValidateDecimalPlaces(
        string value,
        TransactionColumnDefinition definition,
        int rowNumber)
    {
        if (definition.MaxDecimalPlaces is null ||
            string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmedValue = value.Trim();
        var decimalSeparatorIndex = trimmedValue.IndexOf('.');
        var decimalPlaces = decimalSeparatorIndex < 0
            ? 0
            : trimmedValue.Length - decimalSeparatorIndex - 1;

        if (decimalPlaces > definition.MaxDecimalPlaces)
        {
            throw new CsvTransactionFileParseException(
                rowNumber,
                $"{definition.Name} must have {definition.MaxDecimalPlaces} decimal places or fewer.",
                definition.Name);
        }
    }

    private static int ParseInteger(string value, string columnName, int rowNumber)
    {
        if (!int.TryParse(
            value.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var number))
        {
            throw new CsvTransactionFileParseException(
                rowNumber,
                $"{columnName} must be a valid integer number.",
                columnName);
        }

        return number;
    }

    private static string[] ParseDelimitedLine(string line, char delimiter, int rowNumber)
    {
        var columns = new List<string>();
        var currentColumn = new StringBuilder();
        var insideQuotedColumn = false;

        for (var index = 0; index < line.Length; index++)
        {
            var currentCharacter = line[index];

            if (currentCharacter == '"')
            {
                if (insideQuotedColumn &&
                    index + 1 < line.Length &&
                    line[index + 1] == '"')
                {
                    currentColumn.Append('"');
                    index++;
                    continue;
                }

                insideQuotedColumn = !insideQuotedColumn;
                continue;
            }

            if (currentCharacter == delimiter && !insideQuotedColumn)
            {
                columns.Add(currentColumn.ToString());
                currentColumn.Clear();
                continue;
            }

            currentColumn.Append(currentCharacter);
        }

        if (insideQuotedColumn)
        {
            throw new CsvTransactionFileParseException(rowNumber, "Delimited row has an unclosed quoted column.", "Row");
        }

        columns.Add(currentColumn.ToString());

        return columns.ToArray();
    }

    private sealed record TransactionColumnDefinition(
        string Field,
        string Name,
        TransactionColumnType Type,
        bool Required = true,
        string? DateFormat = null,
        string? Pattern = null,
        string? PatternDescription = null,
        int? MinLength = null,
        int? MaxLength = null,
        decimal? MinValue = null,
        decimal? MaxValue = null,
        int? MaxDecimalPlaces = null,
        int? FixedWidthStart = null,
        int? FixedWidthLength = null,
        string[]? AllowedValues = null,
        string Description = "");

    private enum TransactionColumnType
    {
        Text,
        Date,
        Decimal,
        Integer
    }
}
