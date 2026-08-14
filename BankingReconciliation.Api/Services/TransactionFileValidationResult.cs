namespace BankingReconciliation.Api.Services;

public sealed class TransactionFileValidationResult
{
    public int RecordCount { get; init; }
    public IReadOnlyList<CsvTransactionFileParseException> Errors { get; init; } = [];
    public bool IsValid => Errors.Count == 0;
}
