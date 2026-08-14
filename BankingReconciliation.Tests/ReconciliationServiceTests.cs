using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class ReconciliationServiceTests
{
    private readonly ReconciliationService _service = new();

    [Fact]
    public void Compare_ReturnsExpectedSummary_WhenRecordsContainMatchMismatchAndMissingRows()
    {
        var branchRecords = new[]
        {
            CreateRecord(fundCode: "A", transactionNumber: "TX001", quantity: 100, amount: 10000),
            CreateRecord(fundCode: "B", transactionNumber: "TX002", quantity: 50, amount: 5000),
            CreateRecord(fundCode: "C", transactionNumber: "TX003", quantity: 20, amount: 2000)
        };
        var bankRecords = new[]
        {
            CreateRecord(fundCode: "A", transactionNumber: "TX001", quantity: 100, amount: 10000),
            CreateRecord(fundCode: "B", transactionNumber: "TX002", quantity: 45, amount: 5000),
            CreateRecord(fundCode: "D", transactionNumber: "TX004", quantity: 10, amount: 1000)
        };

        var summary = _service.Compare(branchRecords, bankRecords);

        Assert.Equal(3, summary.TotalBranchRecords);
        Assert.Equal(3, summary.TotalBankRecords);
        Assert.Equal(1, summary.MatchedCount);
        Assert.Equal(1, summary.MismatchCount);
        Assert.Equal(1, summary.OnlyInBranchCount);
        Assert.Equal(1, summary.OnlyInBankCount);
        Assert.Equal(4, summary.Results.Count);

        Assert.Contains(summary.Results, result =>
            result.Status == ReconciliationStatus.Matched &&
            result.TransactionNumber == "TX001");
        Assert.Contains(summary.Results, result =>
            result.Status == ReconciliationStatus.QuantityMismatch &&
            result.TransactionNumber == "TX002" &&
            result.QuantityDifference == 5 &&
            result.AmountDifference == 0);
        Assert.Contains(summary.Results, result =>
            result.Status == ReconciliationStatus.OnlyInBranch &&
            result.TransactionNumber == "TX003" &&
            result.BankRecord is null);
        Assert.Contains(summary.Results, result =>
            result.Status == ReconciliationStatus.OnlyInBank &&
            result.TransactionNumber == "TX004" &&
            result.BranchRecord is null);
    }

    [Theory]
    [InlineData(100, 10000, 90, 10000, ReconciliationStatus.QuantityMismatch, 10, 0)]
    [InlineData(100, 10000, 100, 9500, ReconciliationStatus.AmountMismatch, 0, 500)]
    [InlineData(100, 10000, 90, 9500, ReconciliationStatus.QuantityAndAmountMismatch, 10, 500)]
    public void Compare_ClassifiesDifferences_WhenMatchedKeyHasDifferentQuantityOrAmount(
        decimal branchQuantity,
        decimal branchAmount,
        decimal bankQuantity,
        decimal bankAmount,
        ReconciliationStatus expectedStatus,
        decimal expectedQuantityDifference,
        decimal expectedAmountDifference)
    {
        var branchRecords = new[]
        {
            CreateRecord(quantity: branchQuantity, amount: branchAmount)
        };
        var bankRecords = new[]
        {
            CreateRecord(quantity: bankQuantity, amount: bankAmount)
        };

        var summary = _service.Compare(branchRecords, bankRecords);
        var result = Assert.Single(summary.Results);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedQuantityDifference, result.QuantityDifference);
        Assert.Equal(expectedAmountDifference, result.AmountDifference);
        Assert.Equal(1, summary.MismatchCount);
    }

    [Fact]
    public void Compare_UsesConfiguredDecimalPlaces_WhenComparingQuantityAndAmount()
    {
        var service = new ReconciliationService(Options.Create(new ReconciliationComparisonOptions
        {
            QuantityDecimalPlaces = 2,
            AmountDecimalPlaces = 2
        }));
        var branchRecords = new[]
        {
            CreateRecord(quantity: 100.125m, amount: 10000.125m)
        };
        var bankRecords = new[]
        {
            CreateRecord(quantity: 100.126m, amount: 10000.126m)
        };

        var summary = service.Compare(branchRecords, bankRecords);
        var result = Assert.Single(summary.Results);

        Assert.Equal(ReconciliationStatus.Matched, result.Status);
        Assert.Equal(0, result.QuantityDifference);
        Assert.Equal(0, result.AmountDifference);
        Assert.Equal(1, summary.MatchedCount);
        Assert.Equal(0, summary.MismatchCount);
    }

    [Fact]
    public void Compare_TreatsDifferencesWithinConfiguredToleranceAsMatched()
    {
        var service = new ReconciliationService(Options.Create(new ReconciliationComparisonOptions
        {
            QuantityTolerance = 0.5m,
            AmountTolerance = 1m
        }));
        var branchRecords = new[] { CreateRecord(quantity: 100.4m, amount: 10000.75m) };
        var bankRecords = new[] { CreateRecord(quantity: 100m, amount: 10000m) };

        var summary = service.Compare(branchRecords, bankRecords);

        Assert.Equal(ReconciliationStatus.Matched, Assert.Single(summary.Results).Status);
        Assert.True(summary.IsExactMatch);
        Assert.Equal(0, summary.MismatchCount);
    }

    [Fact]
    public void Compare_UsesSourceSpecificDecimalPlaces_WhenConfigured()
    {
        var service = new ReconciliationService(Options.Create(new ReconciliationComparisonOptions
        {
            BranchQuantityDecimalPlaces = 2,
            BankQuantityDecimalPlaces = 3,
            BranchAmountDecimalPlaces = 2,
            BankAmountDecimalPlaces = 3
        }));
        var branchRecords = new[]
        {
            CreateRecord(quantity: 100.12m, amount: 250.12m)
        };
        var bankRecords = new[]
        {
            CreateRecord(quantity: 100.120m, amount: 250.120m)
        };

        var summary = service.Compare(branchRecords, bankRecords);
        var result = Assert.Single(summary.Results);

        Assert.Equal(ReconciliationStatus.Matched, result.Status);
        Assert.Equal(0, result.QuantityDifference);
        Assert.Equal(0, result.AmountDifference);
    }

    [Fact]
    public void Compare_KeepsExactDecimalComparison_WhenDecimalPlacesAreNotConfigured()
    {
        var branchRecords = new[]
        {
            CreateRecord(quantity: 100.125m, amount: 10000.125m)
        };
        var bankRecords = new[]
        {
            CreateRecord(quantity: 100.126m, amount: 10000.126m)
        };

        var summary = _service.Compare(branchRecords, bankRecords);
        var result = Assert.Single(summary.Results);

        Assert.Equal(ReconciliationStatus.QuantityAndAmountMismatch, result.Status);
        Assert.Equal(-0.001m, result.QuantityDifference);
        Assert.Equal(-0.001m, result.AmountDifference);
    }

    [Fact]
    public void Compare_UsesConfiguredMatchingFields_WhenMatchingRecords()
    {
        var service = new ReconciliationService(Options.Create(new ReconciliationComparisonOptions
        {
            MatchingFields = ["BranchCode", "TransactionNumber"]
        }));
        var branchRecords = new[]
        {
            CreateRecord(fundCode: "A", transactionNumber: "TX001", quantity: 100, amount: 10000)
        };
        var bankRecords = new[]
        {
            CreateRecord(fundCode: "B", transactionNumber: "TX001", quantity: 100, amount: 10000)
        };

        var summary = service.Compare(branchRecords, bankRecords);
        var result = Assert.Single(summary.Results);

        Assert.Equal(ReconciliationStatus.Matched, result.Status);
        Assert.Equal(1, summary.MatchedCount);
    }

    [Fact]
    public void Compare_UsesConfiguredMatchingFields_WhenDetectingDuplicates()
    {
        var service = new ReconciliationService(Options.Create(new ReconciliationComparisonOptions
        {
            MatchingFields = ["BranchCode", "TransactionNumber"]
        }));
        var branchRecords = new[]
        {
            CreateRecord(fundCode: "A", transactionNumber: "TX001"),
            CreateRecord(fundCode: "B", transactionNumber: "TX001")
        };
        var bankRecords = new[]
        {
            CreateRecord(fundCode: "A", transactionNumber: "TX001")
        };

        var exception = Assert.Throws<DuplicateTransactionKeyException>(() =>
            service.Compare(branchRecords, bankRecords));

        Assert.Equal("branch", exception.SourceName);
        Assert.Equal("BEYLIKDUZU|TX001", exception.MatchingKey);
    }

    [Fact]
    public void Compare_UsesConfiguredComparisonFields_WhenIgnoringQuantityDifferences()
    {
        var service = new ReconciliationService(Options.Create(new ReconciliationComparisonOptions
        {
            ComparisonFields = ["Amount"]
        }));
        var branchRecords = new[]
        {
            CreateRecord(quantity: 100, amount: 10000)
        };
        var bankRecords = new[]
        {
            CreateRecord(quantity: 90, amount: 10000)
        };

        var summary = service.Compare(branchRecords, bankRecords);
        var result = Assert.Single(summary.Results);

        Assert.Equal(ReconciliationStatus.Matched, result.Status);
        Assert.Equal(0, result.QuantityDifference);
        Assert.Equal(0, result.AmountDifference);
        Assert.Equal(1, summary.MatchedCount);
    }

    [Fact]
    public void Compare_UsesConfiguredComparisonFields_WhenIgnoringAmountDifferences()
    {
        var service = new ReconciliationService(Options.Create(new ReconciliationComparisonOptions
        {
            ComparisonFields = ["Quantity"]
        }));
        var branchRecords = new[]
        {
            CreateRecord(quantity: 100, amount: 10000)
        };
        var bankRecords = new[]
        {
            CreateRecord(quantity: 90, amount: 9500)
        };

        var summary = service.Compare(branchRecords, bankRecords);
        var result = Assert.Single(summary.Results);

        Assert.Equal(ReconciliationStatus.QuantityMismatch, result.Status);
        Assert.Equal(10, result.QuantityDifference);
        Assert.Equal(0, result.AmountDifference);
    }

    [Fact]
    public void Compare_UsesConfiguredResultFields_WhenCreatingResults()
    {
        var service = new ReconciliationService(Options.Create(new ReconciliationComparisonOptions
        {
            ResultFields = ["BranchCode", "TransactionDate", "TransactionNumber"]
        }));
        var branchRecords = new[]
        {
            CreateRecord(transactionNumber: "TX001")
        };
        var bankRecords = new[]
        {
            CreateRecord(transactionNumber: "TX001")
        };

        var summary = service.Compare(branchRecords, bankRecords);
        var result = Assert.Single(summary.Results);

        Assert.Equal("BEYLIKDUZU", result.FieldValues["BranchCode"]);
        Assert.Equal("2026-06-26", result.FieldValues["TransactionDate"]);
        Assert.Equal("TX001", result.FieldValues["TransactionNumber"]);
        Assert.False(result.FieldValues.ContainsKey("FundCode"));
    }

    [Fact]
    public void Compare_UsesConfiguredExtraComparisonFields_WhenClassifyingRecords()
    {
        var service = new ReconciliationService(Options.Create(new ReconciliationComparisonOptions
        {
            ComparisonFields = ["Commission"],
            ResultFields = ["BranchCode", "Commission"]
        }));
        var branchRecords = new[]
        {
            CreateRecord(extraFields: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Commission"] = "12.34"
            })
        };
        var bankRecords = new[]
        {
            CreateRecord(extraFields: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Commission"] = "10.00"
            })
        };

        var summary = service.Compare(branchRecords, bankRecords);
        var result = Assert.Single(summary.Results);

        Assert.Equal(ReconciliationStatus.FieldMismatch, result.Status);
        Assert.Equal(2.34m, result.FieldDifferences["Commission"]);
        Assert.Equal("12.34", result.FieldValues["Commission"]);
        Assert.Equal(1, summary.MismatchCount);
    }

    [Fact]
    public void Compare_ThrowsDuplicateTransactionKeyException_WhenBranchRecordsContainDuplicateKey()
    {
        var branchRecords = new[]
        {
            CreateRecord(),
            CreateRecord()
        };
        var bankRecords = new[]
        {
            CreateRecord()
        };

        var exception = Assert.Throws<DuplicateTransactionKeyException>(() =>
            _service.Compare(branchRecords, bankRecords));

        Assert.Equal("branch", exception.SourceName);
        Assert.Equal("BEYLIKDUZU|A|TX001", exception.MatchingKey);
    }

    [Fact]
    public void Compare_ThrowsDuplicateTransactionKeyException_WhenBankRecordsContainDuplicateKey()
    {
        var branchRecords = new[]
        {
            CreateRecord()
        };
        var bankRecords = new[]
        {
            CreateRecord(),
            CreateRecord()
        };

        var exception = Assert.Throws<DuplicateTransactionKeyException>(() =>
            _service.Compare(branchRecords, bankRecords));

        Assert.Equal("bank", exception.SourceName);
        Assert.Equal("BEYLIKDUZU|A|TX001", exception.MatchingKey);
    }

    private static TransactionRecord CreateRecord(
        string branchCode = "BEYLIKDUZU",
        string fundCode = "A",
        string transactionNumber = "TX001",
        decimal quantity = 100,
        decimal amount = 10000,
        Dictionary<string, string>? extraFields = null)
    {
        return new TransactionRecord
        {
            BranchCode = branchCode,
            FundCode = fundCode,
            TransactionNumber = transactionNumber,
            TransactionDate = new DateOnly(2026, 6, 26),
            Quantity = quantity,
            Amount = amount,
            ExtraFields = extraFields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }
}
