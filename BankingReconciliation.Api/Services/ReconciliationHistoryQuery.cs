using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationHistoryQuery
{
    public string? Search { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public ReconciliationBatchStatus? Status { get; init; }
    public ReconciliationInputType? InputType { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
}
