using BankingReconciliation.Api.Options;

namespace BankingReconciliation.Api.Services;

public interface IReconciliationFileSchemaRepository
{
    ReconciliationFileSchemaOptions? Get();
    void Save(ReconciliationFileSchemaOptions options);
}
