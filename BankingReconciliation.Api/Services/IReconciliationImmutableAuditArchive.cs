using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public interface IReconciliationImmutableAuditArchive
{
    bool Enabled { get; }

    Task<string> WriteAsync(
        IReadOnlyCollection<ReconciliationAuditEvent> events,
        CancellationToken cancellationToken = default);
}

public sealed class DisabledReconciliationImmutableAuditArchive :
    IReconciliationImmutableAuditArchive
{
    public bool Enabled => false;

    public Task<string> WriteAsync(
        IReadOnlyCollection<ReconciliationAuditEvent> events,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Immutable audit archive is disabled.");
}
