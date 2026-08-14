using BankingReconciliation.Api.Services;

namespace BankingReconciliation.Tests;

public class ReconciliationFileJobQueueTests
{
    [Fact]
    public void TryQueue_ReturnsFalse_WhenQueueIsFull()
    {
        var queue = new ReconciliationFileJobQueue(capacity: 1);

        Assert.True(queue.TryQueue(Guid.NewGuid()));
        Assert.False(queue.TryQueue(Guid.NewGuid()));
    }

    [Fact]
    public void TryDequeue_ReturnsQueuedJob()
    {
        var queue = new ReconciliationFileJobQueue(capacity: 1);
        var batchId = Guid.NewGuid();

        Assert.True(queue.TryQueue(batchId));
        Assert.True(queue.TryDequeue(out var dequeuedId));
        Assert.Equal(batchId, dequeuedId);
    }
}
