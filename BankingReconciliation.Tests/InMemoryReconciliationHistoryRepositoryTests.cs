using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Services;

namespace BankingReconciliation.Tests;

public class InMemoryReconciliationHistoryRepositoryTests
{
    [Fact]
    public void GetAll_AppliesSearchDateAndPagingFilters()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryReconciliationHistoryRepository(timeProvider);
        repository.Add("june-branch.csv", "june-bank.csv", 10, new ReconciliationSummary());

        timeProvider.UtcNow = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        repository.Add("daily-branch.csv", "daily-bank.csv", 10, new ReconciliationSummary());

        timeProvider.UtcNow = new DateTimeOffset(2026, 7, 11, 8, 0, 0, TimeSpan.Zero);
        repository.AddFailed("failed-branch.csv", "failed-bank.csv", 10, "ReadFailed", "Source timeout");

        var searchResult = repository.GetAll(new ReconciliationHistoryQuery
        {
            Search = "TIMEOUT",
            Take = 50
        });
        var dateResult = repository.GetAll(new ReconciliationHistoryQuery
        {
            From = new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 7, 10, 23, 59, 59, TimeSpan.Zero),
            Take = 50
        });
        var pagedResult = repository.GetAll(new ReconciliationHistoryQuery
        {
            Skip = 1,
            Take = 1
        });

        Assert.Equal("failed-branch.csv", Assert.Single(searchResult).BranchFileName);
        Assert.Equal("daily-branch.csv", Assert.Single(dateResult).BranchFileName);
        Assert.Equal("daily-branch.csv", Assert.Single(pagedResult).BranchFileName);
        Assert.Equal(1, repository.Count(new ReconciliationHistoryQuery
        {
            Search = "TIMEOUT"
        }));
        Assert.Equal(2, repository.Count(new ReconciliationHistoryQuery
        {
            Status = ReconciliationBatchStatus.Completed
        }));
    }

    [Fact]
    public void DecideApproval_SetsAuditFields_AndPreventsSecondDecision()
    {
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero));
        var repository = new InMemoryReconciliationHistoryRepository(timeProvider);
        var batch = repository.Add("branch.csv", "bank.csv", 10, new ReconciliationSummary());

        var firstResult = repository.DecideApproval(
            batch.Id,
            ReconciliationApprovalDecision.Reject,
            "reviewer",
            "Tutar farki incelenmeli.");
        var secondResult = repository.DecideApproval(
            batch.Id,
            ReconciliationApprovalDecision.Approve,
            "other-reviewer",
            null);

        Assert.Equal(ReconciliationApprovalDecisionOutcome.Updated, firstResult.Outcome);
        Assert.Equal(ReconciliationApprovalStatus.Rejected, batch.ApprovalStatus);
        Assert.Equal("reviewer", batch.DecisionBy);
        Assert.Equal(timeProvider.UtcNow, batch.DecisionAt);
        Assert.Equal("Tutar farki incelenmeli.", batch.DecisionComment);
        Assert.Equal(ReconciliationApprovalDecisionOutcome.AlreadyDecided, secondResult.Outcome);
    }

    [Fact]
    public void JobLease_AllowsOnlyOneOwner_AndRequiresThatOwnerToComplete()
    {
        var now = new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryReconciliationHistoryRepository(new MutableTimeProvider(now));
        var batch = repository.AddQueued(
            "database:BRANCH",
            "database:BANK",
            ReconciliationInputType.DatabaseSources);

        Assert.Contains(
            batch.Id,
            repository.GetClaimableJobIds(ReconciliationInputType.DatabaseSources, now, take: 10));
        Assert.True(repository.TryClaimJob(
            batch.Id,
            ReconciliationInputType.DatabaseSources,
            "worker-1",
            now,
            TimeSpan.FromMinutes(2)));
        Assert.False(repository.TryClaimJob(
            batch.Id,
            ReconciliationInputType.DatabaseSources,
            "worker-2",
            now,
            TimeSpan.FromMinutes(2)));
        Assert.False(repository.RenewJobLease(
            batch.Id,
            "worker-2",
            now.AddSeconds(30),
            TimeSpan.FromMinutes(2)));
        Assert.True(repository.RenewJobLease(
            batch.Id,
            "worker-1",
            now.AddSeconds(30),
            TimeSpan.FromMinutes(2)));
        Assert.False(repository.TryCompleteClaimedJob(
            batch.Id,
            "worker-2",
            processingDurationMilliseconds: 12,
            new ReconciliationSummary()));
        Assert.True(repository.TryCompleteClaimedJob(
            batch.Id,
            "worker-1",
            processingDurationMilliseconds: 12,
            new ReconciliationSummary()));

        var completed = repository.GetById(batch.Id)!;
        Assert.Equal(ReconciliationBatchStatus.Completed, completed.Status);
        Assert.Equal(1, completed.AttemptCount);
        Assert.Null(completed.LeaseOwner);
        Assert.Null(completed.LeaseExpiresAt);
    }

    [Fact]
    public void ExpiredLease_IsReclaimed_AndRetryStopsAtConfiguredAttemptLimit()
    {
        var now = new DateTimeOffset(2026, 7, 22, 11, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryReconciliationHistoryRepository(new MutableTimeProvider(now));
        var batch = repository.AddQueued(
            "database:BRANCH",
            "database:BANK",
            ReconciliationInputType.DatabaseSources);
        Assert.True(repository.TryClaimJob(
            batch.Id,
            ReconciliationInputType.DatabaseSources,
            "worker-1",
            now,
            TimeSpan.FromMinutes(1)));

        var reclaimedAt = now.AddMinutes(2);
        Assert.True(repository.TryClaimJob(
            batch.Id,
            ReconciliationInputType.DatabaseSources,
            "worker-2",
            reclaimedAt,
            TimeSpan.FromMinutes(1)));
        var retryAt = reclaimedAt.AddMinutes(5);
        Assert.Equal(
            ReconciliationJobFailureDisposition.RetryScheduled,
            repository.HandleClaimedJobFailure(
                batch.Id,
                "worker-2",
                processingDurationMilliseconds: 25,
                "DatabaseSourceReadFailed",
                "Temporary timeout.",
                retryable: true,
                maxAttempts: 3,
                retryAt));
        Assert.DoesNotContain(
            batch.Id,
            repository.GetClaimableJobIds(
                ReconciliationInputType.DatabaseSources,
                retryAt.AddTicks(-1),
                take: 10));

        Assert.True(repository.TryClaimJob(
            batch.Id,
            ReconciliationInputType.DatabaseSources,
            "worker-3",
            retryAt,
            TimeSpan.FromMinutes(1)));
        Assert.Equal(
            ReconciliationJobFailureDisposition.Failed,
            repository.HandleClaimedJobFailure(
                batch.Id,
                "worker-3",
                processingDurationMilliseconds: 40,
                "DatabaseSourceReadFailed",
                "Temporary timeout.",
                retryable: true,
                maxAttempts: 3,
                retryAt.AddMinutes(5)));

        var failed = repository.GetById(batch.Id)!;
        Assert.Equal(ReconciliationBatchStatus.Failed, failed.Status);
        Assert.Equal(3, failed.AttemptCount);
        Assert.Equal("DatabaseSourceReadFailed", failed.ErrorCode);
        Assert.Null(failed.NextAttemptAt);
        Assert.Null(failed.LeaseOwner);
    }

    [Fact]
    public void UploadedFileJob_CanOnlyBeClaimedByItsTemporaryStorage()
    {
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryReconciliationHistoryRepository(new MutableTimeProvider(now));
        var owningStorageKey = Guid.NewGuid().ToString("N");
        var otherStorageKey = Guid.NewGuid().ToString("N");
        var batch = repository.AddQueued(
            "branch.csv",
            "bank.csv",
            ReconciliationInputType.UploadedFiles,
            temporaryStorageKey: owningStorageKey);

        Assert.Empty(repository.GetActiveUploadedFileJobIds(
            otherStorageKey,
            [batch.Id]));
        Assert.Contains(
            batch.Id,
            repository.GetActiveUploadedFileJobIds(
                owningStorageKey,
                [batch.Id]));
        Assert.DoesNotContain(
            batch.Id,
            repository.GetClaimableJobIds(
                ReconciliationInputType.UploadedFiles,
                now,
                take: 10,
                temporaryStorageKey: otherStorageKey));
        Assert.False(repository.TryClaimJob(
            batch.Id,
            ReconciliationInputType.UploadedFiles,
            "other-storage-worker",
            now,
            TimeSpan.FromMinutes(2),
            otherStorageKey));
        Assert.Contains(
            batch.Id,
            repository.GetClaimableJobIds(
                ReconciliationInputType.UploadedFiles,
                now,
                take: 10,
                temporaryStorageKey: owningStorageKey));
        Assert.True(repository.TryClaimJob(
            batch.Id,
            ReconciliationInputType.UploadedFiles,
            "owning-storage-worker",
            now,
            TimeSpan.FromMinutes(2),
            owningStorageKey));
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
