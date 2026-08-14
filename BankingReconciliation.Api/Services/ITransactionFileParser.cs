using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public interface ITransactionFileParser
{
    Task<List<TransactionRecord>> ParseAsync(
        IFormFile file,
        CancellationToken cancellationToken = default);

    Task<List<TransactionRecord>> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken = default);

    Task<TransactionFileValidationResult> ValidateAsync(
        IFormFile file,
        CancellationToken cancellationToken = default);
}
