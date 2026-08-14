namespace BankingReconciliation.Api.Services;

public class ReconciliationDatabaseSourceException : Exception
{
    public ReconciliationDatabaseSourceException(string sourceCode, string message)
        : base(message)
    {
        SourceCode = sourceCode;
    }

    public ReconciliationDatabaseSourceException(
        string sourceCode,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        SourceCode = sourceCode;
    }

    public string SourceCode { get; }
}
