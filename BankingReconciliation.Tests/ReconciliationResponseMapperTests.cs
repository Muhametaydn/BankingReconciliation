using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Services;

namespace BankingReconciliation.Tests;

public class ReconciliationResponseMapperTests
{
    [Fact]
    public void ToResponse_MapsSummaryAndResultFields()
    {
        var summary = new ReconciliationSummary
        {
            TotalBranchRecords = 1,
            TotalBankRecords = 1,
            MatchedCount = 0,
            OnlyInBranchCount = 0,
            OnlyInBankCount = 0,
            MismatchCount = 1,
            Results =
            [
                new ReconciliationResult
                {
                    Status = ReconciliationStatus.QuantityMismatch,
                    BranchCode = "BEYLIKDUZU",
                    FundCode = "A",
                    TransactionNumber = "TX001",
                    BranchRecord = CreateRecord(quantity: 100),
                    BankRecord = CreateRecord(quantity: 90),
                    QuantityDifference = 10,
                    AmountDifference = 0,
                    FieldDifferences = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Commission"] = 2.34m
                    },
                    FieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["BranchCode"] = "BEYLIKDUZU",
                        ["TransactionDate"] = "2026-06-26"
                    }
                }
            ]
        };

        var response = summary.ToResponse();
        var result = Assert.Single(response.Results);

        Assert.Equal(1, response.TotalBranchRecords);
        Assert.Equal(1, response.TotalBankRecords);
        Assert.Equal(1, response.MismatchCount);
        Assert.Equal(ReconciliationStatus.QuantityMismatch, result.Status);
        Assert.Equal("BEYLIKDUZU", result.BranchCode);
        Assert.Equal("A", result.FundCode);
        Assert.Equal("TX001", result.TransactionNumber);
        Assert.Equal(10, result.QuantityDifference);
        Assert.Equal(0, result.AmountDifference);
        Assert.Equal(2.34m, result.FieldDifferences["Commission"]);
        Assert.Equal("BEYLIKDUZU", result.FieldValues["BranchCode"]);
        Assert.Equal("2026-06-26", result.FieldValues["TransactionDate"]);
        Assert.NotNull(result.BranchRecord);
        Assert.NotNull(result.BankRecord);
        Assert.Equal(100, result.BranchRecord.Quantity);
        Assert.Equal(90, result.BankRecord.Quantity);
        Assert.Equal("12.34", result.BranchRecord.ExtraFields["Commission"]);
    }

    [Fact]
    public void ToResponse_KeepsMissingSideAsNull()
    {
        var summary = new ReconciliationSummary
        {
            TotalBranchRecords = 1,
            TotalBankRecords = 0,
            OnlyInBranchCount = 1,
            Results =
            [
                new ReconciliationResult
                {
                    Status = ReconciliationStatus.OnlyInBranch,
                    BranchCode = "BEYLIKDUZU",
                    FundCode = "A",
                    TransactionNumber = "TX001",
                    BranchRecord = CreateRecord(),
                    BankRecord = null
                }
            ]
        };

        var response = summary.ToResponse();
        var result = Assert.Single(response.Results);

        Assert.NotNull(result.BranchRecord);
        Assert.Null(result.BankRecord);
    }

    [Fact]
    public void ToDetailResponse_MapsApprovalAuditFields()
    {
        var decidedAt = new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);
        var batch = new ReconciliationBatch
        {
            Id = Guid.NewGuid(),
            ApprovalStatus = ReconciliationApprovalStatus.Approved,
            DecisionBy = "reviewer",
            DecisionAt = decidedAt,
            DecisionComment = "Kontrol edildi."
        };

        var response = batch.ToDetailResponse();

        Assert.Equal(ReconciliationApprovalStatus.Approved, response.ApprovalStatus);
        Assert.Equal("reviewer", response.DecisionBy);
        Assert.Equal(decidedAt, response.DecisionAt);
        Assert.Equal("Kontrol edildi.", response.DecisionComment);
    }

    private static TransactionRecord CreateRecord(decimal quantity = 100)
    {
        return new TransactionRecord
        {
            BranchCode = "BEYLIKDUZU",
            FundCode = "A",
            TransactionNumber = "TX001",
            TransactionDate = new DateOnly(2026, 6, 26),
            Quantity = quantity,
            Amount = 10000,
            ExtraFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Commission"] = "12.34"
            }
        };
    }
}
