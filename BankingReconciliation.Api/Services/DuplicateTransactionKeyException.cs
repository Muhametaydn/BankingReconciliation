namespace BankingReconciliation.Api.Services;

public class DuplicateTransactionKeyException : Exception
{
    public DuplicateTransactionKeyException(string sourceName, string matchingKey)
        : base($"Duplicate transaction key '{matchingKey}' was found in {sourceName} records.")
    {
        SourceName = sourceName;
        MatchingKey = matchingKey;
    }

    public string SourceName { get; }
    public string MatchingKey { get; }
}
