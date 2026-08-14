using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

internal interface IReconciliationAuditArchiveSigner
{
    Task<AuditArchiveSignature> SignAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}

internal sealed class LocalReconciliationAuditArchiveSigner :
    IReconciliationAuditArchiveSigner
{
    private readonly ReconciliationImmutableAuditArchiveOptions _options;

    public LocalReconciliationAuditArchiveSigner(
        IOptions<ReconciliationImmutableAuditArchiveOptions> options)
    {
        _options = options.Value;
    }

    public Task<AuditArchiveSignature> SignAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReconciliationAuditArchiveSigner.Sign(payload.Span, _options));
    }
}
