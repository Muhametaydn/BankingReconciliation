namespace BankingReconciliation.Api.Services;

public class ReconciliationTemporaryFileException : Exception
{
    public ReconciliationTemporaryFileException(string message)
        : base(message)
    {
    }

    public ReconciliationTemporaryFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ReconciliationTemporaryFileLimitException : ReconciliationTemporaryFileException
{
    public ReconciliationTemporaryFileLimitException(long maxFileSizeBytes)
        : base($"Uploaded file exceeds the maximum of {maxFileSizeBytes} bytes.")
    {
        MaxFileSizeBytes = maxFileSizeBytes;
    }

    public long MaxFileSizeBytes { get; }
}
