using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public interface IReconciliationService
{
    ReconciliationSummary Compare(
        IReadOnlyCollection<TransactionRecord> branchRecords,
        IReadOnlyCollection<TransactionRecord> bankRecords);
}
