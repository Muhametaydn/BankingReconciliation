using System.Data;
using System.Globalization;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BankingReconciliation.Api.Services;

public class PostgresReconciliationDatabaseSourceReader : IReconciliationDatabaseSourceReader
{
    private static readonly HashSet<string> CoreFields =
    [
        "BranchCode",
        "FundCode",
        "TransactionNumber",
        "TransactionDate",
        "Quantity",
        "Amount"
    ];

    private readonly IConfiguration _configuration;
    private readonly ReconciliationDatabaseSourcesOptions _databaseOptions;
    private readonly ReconciliationFileSchemaStore _fileSchemaStore;
    private readonly ReconciliationComparisonOptionsStore _comparisonOptionsStore;

    public PostgresReconciliationDatabaseSourceReader(
        IConfiguration configuration,
        IOptions<ReconciliationDatabaseSourcesOptions> databaseOptions,
        ReconciliationFileSchemaStore fileSchemaStore,
        ReconciliationComparisonOptionsStore comparisonOptionsStore)
    {
        _configuration = configuration;
        _databaseOptions = databaseOptions.Value;
        _fileSchemaStore = fileSchemaStore;
        _comparisonOptionsStore = comparisonOptionsStore;
    }

    public async Task<IReadOnlyList<TransactionRecord>> ReadAsync(
        string sourceCode,
        CancellationToken cancellationToken = default)
    {
        var source = _databaseOptions.Sources.SingleOrDefault(item =>
            string.Equals(item.Code, sourceCode, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            throw new ReconciliationDatabaseSourceException(
                sourceCode,
                $"Database source '{sourceCode}' is not configured.");
        }

        var connectionString = _configuration.GetConnectionString(source.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ReconciliationDatabaseSourceException(
                sourceCode,
                $"Connection string for database source '{sourceCode}' is not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            await using (var readOnlyCommand = new NpgsqlCommand(
                "SET TRANSACTION READ ONLY",
                connection,
                transaction))
            {
                await readOnlyCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new NpgsqlCommand(source.Query, connection, transaction)
            {
                CommandTimeout = _databaseOptions.CommandTimeoutSeconds
            };
            IReadOnlyList<TransactionRecord> records;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                records = await ReadRecordsAsync(sourceCode, reader, cancellationToken);
            }

            await transaction.RollbackAsync(cancellationToken);
            return records;
        }
        catch (ReconciliationDatabaseSourceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReconciliationDatabaseSourceException(
                sourceCode,
                $"Database source '{sourceCode}' could not be read.",
                exception);
        }
    }

    private async Task<IReadOnlyList<TransactionRecord>> ReadRecordsAsync(
        string sourceCode,
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var schema = _fileSchemaStore.GetOptions().GetEffectiveColumns();
        var ordinals = GetOrdinals(sourceCode, reader, schema);
        var comparisonOptions = _comparisonOptionsStore.GetOptions();
        var records = new List<TransactionRecord>();
        var rowNumber = 0;

        while (await reader.ReadAsync(cancellationToken))
        {
            rowNumber++;
            if (rowNumber > _databaseOptions.MaxRecordsPerSource)
            {
                throw new ReconciliationDatabaseSourceException(
                    sourceCode,
                    $"Database source '{sourceCode}' exceeds the maximum of {_databaseOptions.MaxRecordsPerSource} records.");
            }

            var values = schema.ToDictionary(
                column => column.Field,
                column => ReadValue(reader, ordinals[column.Field], column.DateFormat),
                StringComparer.OrdinalIgnoreCase);
            records.Add(CreateRecord(sourceCode, rowNumber, values, comparisonOptions));
        }

        return records;
    }

    private static Dictionary<string, int> GetOrdinals(
        string sourceCode,
        NpgsqlDataReader reader,
        IReadOnlyCollection<ReconciliationFileSchemaColumnOptions> schema)
    {
        var availableColumns = Enumerable.Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, ordinal => ordinal, StringComparer.OrdinalIgnoreCase);
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in schema)
        {
            if (availableColumns.TryGetValue(column.Field, out var ordinal) ||
                availableColumns.TryGetValue(column.Name, out ordinal))
            {
                ordinals[column.Field] = ordinal;
                continue;
            }

            if (column.Required)
            {
                throw new ReconciliationDatabaseSourceException(
                    sourceCode,
                    $"Database source '{sourceCode}' is missing required column '{column.Field}'.");
            }

            ordinals[column.Field] = -1;
        }

        return ordinals;
    }

    private static string ReadValue(
        NpgsqlDataReader reader,
        int ordinal,
        string? dateFormat)
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return string.Empty;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly date => date.ToString(dateFormat ?? "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => DateOnly.FromDateTime(dateTime)
                .ToString(dateFormat ?? "yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static TransactionRecord CreateRecord(
        string sourceCode,
        int rowNumber,
        IReadOnlyDictionary<string, string> values,
        ReconciliationComparisonOptions options)
    {
        return new TransactionRecord
        {
            BranchCode = NormalizeMappedText(
                "BranchCode",
                Required(values, "BranchCode", sourceCode, rowNumber),
                options.BranchCodeMappings,
                options,
                options.TrimBranchCode ?? options.TrimTextValues),
            FundCode = NormalizeMappedText(
                "FundCode",
                Required(values, "FundCode", sourceCode, rowNumber),
                options.FundCodeMappings,
                options,
                options.TrimFundCode ?? options.TrimTextValues),
            TransactionNumber = NormalizeMappedText(
                "TransactionNumber",
                Required(values, "TransactionNumber", sourceCode, rowNumber),
                options.TransactionNumberMappings,
                options,
                options.TrimTransactionNumber ?? options.TrimTextValues),
            TransactionDate = ParseDate(values["TransactionDate"], sourceCode, rowNumber),
            Quantity = ParseDecimal(values["Quantity"], "Quantity", sourceCode, rowNumber),
            Amount = ParseDecimal(values["Amount"], "Amount", sourceCode, rowNumber),
            ExtraFields = values
                .Where(value => !CoreFields.Contains(value.Key))
                .ToDictionary(
                    value => value.Key,
                    value => value.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string Required(
        IReadOnlyDictionary<string, string> values,
        string field,
        string sourceCode,
        int rowNumber)
    {
        var value = values[field];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ReconciliationDatabaseSourceException(
                sourceCode,
                $"Database source '{sourceCode}' row {rowNumber} requires '{field}'.");
        }

        return value;
    }

    private static DateOnly ParseDate(string value, string sourceCode, int rowNumber)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new ReconciliationDatabaseSourceException(
            sourceCode,
            $"Database source '{sourceCode}' row {rowNumber} has an invalid TransactionDate.");
    }

    private static decimal ParseDecimal(
        string value,
        string field,
        string sourceCode,
        int rowNumber)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        throw new ReconciliationDatabaseSourceException(
            sourceCode,
            $"Database source '{sourceCode}' row {rowNumber} has an invalid {field}.");
    }

    private static string NormalizeMappedText(
        string field,
        string value,
        IReadOnlyDictionary<string, string> specificMappings,
        ReconciliationComparisonOptions options,
        bool trimValue)
    {
        var normalizedValue = trimValue ? value.Trim() : value;
        var genericMappings = options.FieldMappings.FirstOrDefault(mapping =>
            string.Equals(mapping.Key, field, StringComparison.OrdinalIgnoreCase)).Value;

        if (TryGetMappingValue(specificMappings, normalizedValue, out var mappedValue) ||
            TryGetMappingValue(genericMappings, normalizedValue, out mappedValue))
        {
            normalizedValue = trimValue ? mappedValue.Trim() : mappedValue;
        }

        return options.NormalizeCodeCase
            ? normalizedValue.ToUpperInvariant()
            : normalizedValue;
    }

    private static bool TryGetMappingValue(
        IReadOnlyDictionary<string, string>? mappings,
        string key,
        out string mappedValue)
    {
        if (mappings is not null)
        {
            foreach (var mapping in mappings)
            {
                if (string.Equals(mapping.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    mappedValue = mapping.Value;
                    return true;
                }
            }
        }

        mappedValue = string.Empty;
        return false;
    }
}
