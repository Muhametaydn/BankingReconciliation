using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Api.Services;

public interface IReconciliationComparisonOptionsRepository
{
    ReconciliationComparisonOptions? Get();
    void Save(ReconciliationComparisonOptions options);
}
