using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public interface IReconciliationSourceRepository
{
    IReadOnlyCollection<ReconciliationSource> GetAll();
    ReconciliationSource? Update(
        Guid id,
        string displayName,
        string description,
        bool isActive);
}
