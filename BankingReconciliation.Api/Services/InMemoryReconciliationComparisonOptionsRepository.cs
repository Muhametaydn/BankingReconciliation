using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Api.Services;

public class InMemoryReconciliationComparisonOptionsRepository : IReconciliationComparisonOptionsRepository
{
    private readonly object _lock = new();
    private ReconciliationComparisonOptions? _options;

    public ReconciliationComparisonOptions? Get()
    {
        lock (_lock)
        {
            return _options is null ? null : ReconciliationComparisonOptionsStore.Clone(_options);
        }
    }

    public void Save(ReconciliationComparisonOptions options)
    {
        lock (_lock)
        {
            _options = ReconciliationComparisonOptionsStore.Clone(options);
        }
    }
}
