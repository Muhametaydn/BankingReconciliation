using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public interface IReconciliationDatabaseSourceReader
{
    Task<IReadOnlyList<TransactionRecord>> ReadAsync(
        string sourceCode,
        CancellationToken cancellationToken = default);
}
