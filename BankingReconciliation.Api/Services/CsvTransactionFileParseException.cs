namespace BankingReconciliation.Api.Services;

public class CsvTransactionFileParseException : Exception
{
    public CsvTransactionFileParseException(int rowNumber, string message, string? columnName = null)
        : base($"CSV row {rowNumber}: {message}")
    {
        RowNumber = rowNumber;
        ColumnName = columnName;
    }

    public int RowNumber { get; }
    public string? ColumnName { get; }
}
