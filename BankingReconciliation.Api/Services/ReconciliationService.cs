using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace BankingReconciliation.Api.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly ReconciliationComparisonOptions _comparisonOptions;
    private readonly string[] _matchingFields;
    private readonly string[] _comparisonFields;
    private readonly string[] _resultFields;

    public ReconciliationService()
        : this(Microsoft.Extensions.Options.Options.Create(new ReconciliationComparisonOptions()))
    {
    }

    public ReconciliationService(IOptions<ReconciliationComparisonOptions> comparisonOptions)
    {
        _comparisonOptions = comparisonOptions.Value;
        _matchingFields = GetEffectiveMatchingFields(_comparisonOptions);
        _comparisonFields = GetEffectiveComparisonFields(_comparisonOptions);
        _resultFields = GetEffectiveResultFields(_comparisonOptions);
    }

    public ReconciliationSummary Compare(
        IReadOnlyCollection<TransactionRecord> branchRecords,
        IReadOnlyCollection<TransactionRecord> bankRecords)
    {
        var results = new List<ReconciliationResult>();

        EnsureUniqueMatchingKeys(branchRecords, "branch");
        var bankRecordsByKey = CreateUniqueLookup(bankRecords, "bank");
        var processedBankKeys = new HashSet<string>();

        foreach (var branchRecord in branchRecords)
        {
            var branchMatchingKey = CreateMatchingKey(branchRecord);
            if (!bankRecordsByKey.TryGetValue(branchMatchingKey, out var bankRecord))
            {
                results.Add(CreateOnlyInBranchResult(branchRecord));
                continue;
            }

            processedBankKeys.Add(CreateMatchingKey(bankRecord));
            results.Add(CompareMatchedRecords(branchRecord, bankRecord));
        }

        foreach (var bankRecord in bankRecords)
        {
            if (!processedBankKeys.Contains(CreateMatchingKey(bankRecord)))
            {
                results.Add(CreateOnlyInBankResult(bankRecord));
            }
        }

        var mismatchCount = results.Count(result =>
            result.Status is ReconciliationStatus.QuantityMismatch
                or ReconciliationStatus.AmountMismatch
                or ReconciliationStatus.QuantityAndAmountMismatch
                or ReconciliationStatus.FieldMismatch);
        var onlyInBranchCount = results.Count(result => result.Status == ReconciliationStatus.OnlyInBranch);
        var onlyInBankCount = results.Count(result => result.Status == ReconciliationStatus.OnlyInBank);

        return new ReconciliationSummary
        {
            TotalBranchRecords = branchRecords.Count,
            TotalBankRecords = bankRecords.Count,
            MatchedCount = results.Count(result => result.Status == ReconciliationStatus.Matched),
            OnlyInBranchCount = onlyInBranchCount,
            OnlyInBankCount = onlyInBankCount,
            MismatchCount = mismatchCount,
            IsExactMatch = mismatchCount == 0 && onlyInBranchCount == 0 && onlyInBankCount == 0 &&
                branchRecords.Count == bankRecords.Count,
            Results = results
        };
    }

    private ReconciliationResult CompareMatchedRecords(
        TransactionRecord branchRecord,
        TransactionRecord bankRecord)
    {
        var quantityDifference = ShouldCompareField("Quantity")
            ? CalculateDifference(
                branchRecord.Quantity,
                bankRecord.Quantity,
                _comparisonOptions.BranchQuantityDecimalPlaces ?? _comparisonOptions.QuantityDecimalPlaces,
                _comparisonOptions.BankQuantityDecimalPlaces ?? _comparisonOptions.QuantityDecimalPlaces)
            : 0;
        var amountDifference = ShouldCompareField("Amount")
            ? CalculateDifference(
                branchRecord.Amount,
                bankRecord.Amount,
                _comparisonOptions.BranchAmountDecimalPlaces ?? _comparisonOptions.AmountDecimalPlaces,
                _comparisonOptions.BankAmountDecimalPlaces ?? _comparisonOptions.AmountDecimalPlaces)
            : 0;
        var fieldDifferences = CalculateExtraFieldDifferences(branchRecord, bankRecord);
        var quantityMismatch = ShouldCompareField("Quantity") &&
            Math.Abs(quantityDifference) > _comparisonOptions.QuantityTolerance;
        var amountMismatch = ShouldCompareField("Amount") &&
            Math.Abs(amountDifference) > _comparisonOptions.AmountTolerance;

        var status = (quantityMismatch, amountMismatch, fieldDifferences.Any(difference => difference.Value != 0)) switch
        {
            (false, false, false) => ReconciliationStatus.Matched,
            (false, false, true) => ReconciliationStatus.FieldMismatch,
            (true, false, _) => ReconciliationStatus.QuantityMismatch,
            (false, true, _) => ReconciliationStatus.AmountMismatch,
            _ => ReconciliationStatus.QuantityAndAmountMismatch
        };

        return new ReconciliationResult
        {
            Status = status,
            BranchCode = branchRecord.BranchCode,
            FundCode = branchRecord.FundCode,
            TransactionNumber = branchRecord.TransactionNumber,
            BranchRecord = branchRecord,
            BankRecord = bankRecord,
            QuantityDifference = quantityDifference,
            AmountDifference = amountDifference,
            FieldDifferences = fieldDifferences,
            FieldValues = CreateFieldValues(branchRecord)
        };
    }

    private static decimal CalculateDifference(
        decimal branchValue,
        decimal bankValue,
        int? branchDecimalPlaces,
        int? bankDecimalPlaces)
    {
        return RoundIfConfigured(branchValue, branchDecimalPlaces) -
            RoundIfConfigured(bankValue, bankDecimalPlaces);
    }

    private static decimal RoundIfConfigured(decimal value, int? decimalPlaces)
    {
        return decimalPlaces is null
            ? value
            : decimal.Round(value, decimalPlaces.Value, MidpointRounding.AwayFromZero);
    }

    private Dictionary<string, decimal> CalculateExtraFieldDifferences(
        TransactionRecord branchRecord,
        TransactionRecord bankRecord)
    {
        return _comparisonFields
            .Where(field =>
                !string.Equals(field, "Quantity", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(field, "Amount", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                field => field,
                field => ParseDecimalField(branchRecord, field) - ParseDecimalField(bankRecord, field),
                StringComparer.OrdinalIgnoreCase);
    }

    private static decimal ParseDecimalField(TransactionRecord record, string field)
    {
        return decimal.Parse(record.GetFieldValue(field), CultureInfo.InvariantCulture);
    }

    private ReconciliationResult CreateOnlyInBranchResult(TransactionRecord branchRecord)
    {
        return new ReconciliationResult
        {
            Status = ReconciliationStatus.OnlyInBranch,
            BranchCode = branchRecord.BranchCode,
            FundCode = branchRecord.FundCode,
            TransactionNumber = branchRecord.TransactionNumber,
            BranchRecord = branchRecord,
            FieldValues = CreateFieldValues(branchRecord)
        };
    }

    private ReconciliationResult CreateOnlyInBankResult(TransactionRecord bankRecord)
    {
        return new ReconciliationResult
        {
            Status = ReconciliationStatus.OnlyInBank,
            BranchCode = bankRecord.BranchCode,
            FundCode = bankRecord.FundCode,
            TransactionNumber = bankRecord.TransactionNumber,
            BankRecord = bankRecord,
            FieldValues = CreateFieldValues(bankRecord)
        };
    }

    private Dictionary<string, TransactionRecord> CreateUniqueLookup(
        IReadOnlyCollection<TransactionRecord> records,
        string sourceName)
    {
        EnsureUniqueMatchingKeys(records, sourceName);

        return records.ToDictionary(CreateMatchingKey);
    }

    private void EnsureUniqueMatchingKeys(
        IReadOnlyCollection<TransactionRecord> records,
        string sourceName)
    {
        var duplicateKey = records
            .GroupBy(CreateMatchingKey)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateKey is not null)
        {
            throw new DuplicateTransactionKeyException(sourceName, duplicateKey);
        }
    }

    private string CreateMatchingKey(TransactionRecord record)
    {
        return record.CreateMatchingKey(_matchingFields);
    }

    private static string[] GetEffectiveMatchingFields(ReconciliationComparisonOptions options)
    {
        return options.MatchingFields.Length == 0
            ? ["BranchCode", "FundCode", "TransactionNumber"]
            : options.MatchingFields.Select(field => field.Trim()).ToArray();
    }

    private bool ShouldCompareField(string field)
    {
        return _comparisonFields.Contains(field, StringComparer.OrdinalIgnoreCase);
    }

    private static string[] GetEffectiveComparisonFields(ReconciliationComparisonOptions options)
    {
        return options.ComparisonFields.Length == 0
            ? ["Quantity", "Amount"]
            : options.ComparisonFields.Select(field => field.Trim()).ToArray();
    }

    private Dictionary<string, string> CreateFieldValues(TransactionRecord record)
    {
        return _resultFields.ToDictionary(
            field => field,
            record.GetFieldValue,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string[] GetEffectiveResultFields(ReconciliationComparisonOptions options)
    {
        return options.ResultFields.Length == 0
            ? ["BranchCode", "FundCode", "TransactionNumber"]
            : options.ResultFields.Select(field => field.Trim()).ToArray();
    }
}
