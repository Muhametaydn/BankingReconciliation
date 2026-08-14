namespace BankingReconciliation.Api.Services;

public interface IReconciliationDatabaseSourceConfiguration
{
    bool IsConfigured(string sourceCode);
}
