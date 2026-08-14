using System.Diagnostics;
using System.Text;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Services;
using Microsoft.AspNetCore.Http;

namespace BankingReconciliation.Tests;

public class ReconciliationPerformanceTests
{
    private static readonly TimeSpan MaximumExpectedDuration = TimeSpan.FromSeconds(15);
    private readonly ITestOutputHelper _output;

    public ReconciliationPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(25_000)]
    [InlineData(75_000)]
    [Trait("Category", "Performance")]
    public void Compare_CompletesWithinBaseline_ForConfiguredVolume(int recordCount)
    {
        var branchRecords = Enumerable.Range(0, recordCount)
            .Select(index => CreateRecord(index))
            .ToArray();
        var bankRecords = Enumerable.Range(0, recordCount - 1)
            .Select(index => CreateRecord(index, amountOffset: index == 100 ? 1 : 0))
            .Append(CreateRecord(recordCount))
            .ToArray();
        var service = new ReconciliationService();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();

        var summary = service.Compare(branchRecords, bankRecords);

        stopwatch.Stop();
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        _output.WriteLine(
            "Comparison: {0:N0} + {1:N0} rows, {2:N0} ms, {3:N1} MB allocated.",
            branchRecords.Length,
            bankRecords.Length,
            stopwatch.ElapsedMilliseconds,
            allocatedBytes / 1024d / 1024d);

        Assert.Equal(recordCount - 2, summary.MatchedCount);
        Assert.Equal(1, summary.MismatchCount);
        Assert.Equal(1, summary.OnlyInBranchCount);
        Assert.Equal(1, summary.OnlyInBankCount);
        Assert.True(
            stopwatch.Elapsed < MaximumExpectedDuration,
            $"Comparison exceeded the {MaximumExpectedDuration.TotalSeconds:N0} second baseline.");
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(50_000)]
    [Trait("Category", "Performance")]
    public async Task ParseAsync_CompletesWithinBaseline_ForConfiguredVolume(int recordCount)
    {
        var csv = new StringBuilder(
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount\n");
        for (var index = 0; index < recordCount; index++)
        {
            csv.Append("BEYLIKDUZU,A,TX")
                .Append(index.ToString("D6"))
                .Append(",2026-06-26,")
                .Append(index)
                .Append(',')
                .Append(index * 10)
                .Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, stream.Length, "file", "large-transactions.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
        var parser = new CsvTransactionFileParser();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();

        var records = await parser.ParseAsync(file);

        stopwatch.Stop();
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        _output.WriteLine(
            "Parser: {0:N0} rows, {1:N0} ms, {2:N1} MB allocated.",
            records.Count,
            stopwatch.ElapsedMilliseconds,
            allocatedBytes / 1024d / 1024d);

        Assert.Equal(recordCount, records.Count);
        Assert.Equal($"TX{recordCount - 1:D6}", records[^1].TransactionNumber);
        Assert.True(
            stopwatch.Elapsed < MaximumExpectedDuration,
            $"Parser exceeded the {MaximumExpectedDuration.TotalSeconds:N0} second baseline.");
    }

    private static TransactionRecord CreateRecord(int index, decimal amountOffset = 0)
    {
        return new TransactionRecord
        {
            BranchCode = "BEYLIKDUZU",
            FundCode = "A",
            TransactionNumber = $"TX{index:D6}",
            TransactionDate = new DateOnly(2026, 6, 26),
            Quantity = index,
            Amount = index * 10m + amountOffset
        };
    }
}
