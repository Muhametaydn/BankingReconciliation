using System.Threading.Channels;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class ReconciliationFileJobQueue
{
    private readonly Channel<Guid> _jobs;

    public ReconciliationFileJobQueue(IOptions<ReconciliationUploadOptions> uploadOptions)
        : this(uploadOptions.Value.BackgroundQueueCapacity)
    {
    }

    public ReconciliationFileJobQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _jobs = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryQueue(Guid batchId) => _jobs.Writer.TryWrite(batchId);

    public bool TryDequeue(out Guid batchId) => _jobs.Reader.TryRead(out batchId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _jobs.Reader.ReadAllAsync(cancellationToken);
}
