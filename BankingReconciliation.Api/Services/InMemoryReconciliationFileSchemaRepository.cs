using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Api.Services;

public class InMemoryReconciliationFileSchemaRepository : IReconciliationFileSchemaRepository
{
    private readonly object _lock = new();
    private ReconciliationFileSchemaOptions? _options;

    public ReconciliationFileSchemaOptions? Get()
    {
        lock (_lock)
        {
            return _options is null ? null : ReconciliationFileSchemaStore.Clone(_options);
        }
    }

    public void Save(ReconciliationFileSchemaOptions options)
    {
        lock (_lock)
        {
            _options = ReconciliationFileSchemaStore.Clone(options);
        }
    }
}
