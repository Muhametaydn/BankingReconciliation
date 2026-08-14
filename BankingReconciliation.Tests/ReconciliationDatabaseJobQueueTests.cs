using BankingReconciliation.Api.Services;

namespace BankingReconciliation.Tests;

public class ReconciliationDatabaseJobQueueTests
{
    [Fact]
    public void TryQueue_ReturnsFalse_WhenBoundedQueueIsFull()
    {
        var queue = new ReconciliationDatabaseJobQueue();

        for (var index = 0; index < 100; index++)
        {
            Assert.True(queue.TryQueue(Guid.NewGuid()));
        }

        Assert.False(queue.TryQueue(Guid.NewGuid()));
    }

    [Fact]
    public void TryDequeue_ReturnsQueuedJob()
    {
        var queue = new ReconciliationDatabaseJobQueue();
        var batchId = Guid.NewGuid();

        Assert.True(queue.TryQueue(batchId));
        Assert.True(queue.TryDequeue(out var dequeuedId));
        Assert.Equal(batchId, dequeuedId);
    }
}
