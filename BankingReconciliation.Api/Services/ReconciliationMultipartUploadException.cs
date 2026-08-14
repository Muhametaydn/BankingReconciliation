namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationMultipartUploadException : Exception
{
    public ReconciliationMultipartUploadException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
