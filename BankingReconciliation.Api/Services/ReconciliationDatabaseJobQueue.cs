using System.Threading.Channels;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationDatabaseJobQueue
{
    private readonly Channel<Guid> _jobs = Channel.CreateBounded<Guid>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });

    public bool TryQueue(Guid batchId) => _jobs.Writer.TryWrite(batchId);

    public bool TryDequeue(out Guid batchId) => _jobs.Reader.TryRead(out batchId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _jobs.Reader.ReadAllAsync(cancellationToken);
}
