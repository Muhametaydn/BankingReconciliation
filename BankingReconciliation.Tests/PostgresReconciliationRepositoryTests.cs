using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace BankingReconciliation.Tests;

public class PostgresReconciliationRepositoryTests
{
    private const string ConnectionStringEnvironmentVariable =
        "BANKING_RECONCILIATION_POSTGRES_TEST_CONNECTION";

    [Fact]
    public void AddAndReadBatch_RoundTripsThroughPostgres_WhenConnectionStringIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        using var dbContext = CreateDbContext(connectionString);
        dbContext.Database.Migrate();

        var repository = new PostgresReconciliationHistoryRepository(dbContext, TimeProvider.System);
        var batch = repository.Add(
            branchFileName: "branch-postgres-test.csv",
            bankFileName: "bank-postgres-test.csv",
            processingDurationMilliseconds: 123,
            summary: CreateSummary());

        try
        {
            var storedBatch = repository.GetById(batch.Id);

            Assert.NotNull(storedBatch);
            Assert.Equal(batch.Id, storedBatch.Id);
            Assert.Equal(ReconciliationBatchStatus.Completed, storedBatch.Status);
            Assert.Equal(123, storedBatch.ProcessingDurationMilliseconds);
            Assert.Equal(2, storedBatch.Summary.TotalBranchRecords);
            Assert.Equal(2, storedBatch.Summary.TotalBankRecords);
            Assert.Equal(1, storedBatch.Summary.MatchedCount);
            Assert.Equal(1, storedBatch.Summary.MismatchCount);

            var storedDifference = Assert.Single(storedBatch.Summary.Results);
            Assert.Equal(ReconciliationStatus.QuantityMismatch, storedDifference.Status);
            Assert.Equal("TX002", storedDifference.TransactionNumber);
            Assert.Equal(5, storedDifference.QuantityDifference);
            Assert.Equal("12.34", storedDifference.BranchRecord?.ExtraFields["Commission"]);
            Assert.Equal("10.00", storedDifference.BankRecord?.ExtraFields["Commission"]);
            Assert.Equal(2.34m, storedDifference.FieldDifferences["Commission"]);
        }
        finally
        {
            var storedEntity = dbContext.ReconciliationBatches
                .Include(entity => entity.Differences)
                .FirstOrDefault(entity => entity.Id == batch.Id);

            if (storedEntity is not null)
            {
                dbContext.ReconciliationBatches.Remove(storedEntity);
                dbContext.SaveChanges();
            }
        }
    }

    [Fact]
    public void GetAll_ReturnsSeededSourcesThroughPostgres_WhenConnectionStringIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        using var dbContext = CreateDbContext(connectionString);
        dbContext.Database.Migrate();

        var repository = new PostgresReconciliationSourceRepository(dbContext);
        var sources = repository.GetAll();

        Assert.Contains(sources, source =>
            source.Type == ReconciliationSourceType.Branch &&
            source.Code == "BRANCH");
        Assert.Contains(sources, source =>
            source.Type == ReconciliationSourceType.Bank &&
            source.Code == "BANK");
    }

    [Fact]
    public void AddFailedAndReadBatch_RoundTripsThroughPostgres_WhenConnectionStringIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        using var dbContext = CreateDbContext(connectionString);
        dbContext.Database.Migrate();

        var repository = new PostgresReconciliationHistoryRepository(dbContext, TimeProvider.System);
        var batch = repository.AddFailed(
            branchFileName: "branch-postgres-failed-test.csv",
            bankFileName: "bank-postgres-failed-test.csv",
            processingDurationMilliseconds: 45,
            errorCode: "InvalidCsvFile",
            errorMessage: "Quantity must be a valid decimal number.");

        try
        {
            var storedBatch = repository.GetById(batch.Id);

            Assert.NotNull(storedBatch);
            Assert.Equal(ReconciliationBatchStatus.Failed, storedBatch.Status);
            Assert.Equal(45, storedBatch.ProcessingDurationMilliseconds);
            Assert.Equal("InvalidCsvFile", storedBatch.ErrorCode);
            Assert.Equal("Quantity must be a valid decimal number.", storedBatch.ErrorMessage);
            Assert.Equal(0, storedBatch.Summary.TotalBranchRecords);
            Assert.Empty(storedBatch.Summary.Results);
        }
        finally
        {
            var storedEntity = dbContext.ReconciliationBatches
                .FirstOrDefault(entity => entity.Id == batch.Id);

            if (storedEntity is not null)
            {
                dbContext.ReconciliationBatches.Remove(storedEntity);
                dbContext.SaveChanges();
            }
        }
    }

    [Fact]
    public void TryClaimJob_AllowsOnePostgresOwner_WhenClaimsRace()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        Guid batchId;
        using (var setupContext = CreateDbContext(connectionString))
        {
            setupContext.Database.Migrate();
            var setupRepository = new PostgresReconciliationHistoryRepository(
                setupContext,
                TimeProvider.System);
            batchId = setupRepository.AddQueued(
                "database:BRANCH",
                "database:BANK",
                ReconciliationInputType.DatabaseSources).Id;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var successfulClaims = 0;
            Parallel.ForEach(new[] { "postgres-worker-1", "postgres-worker-2" }, owner =>
            {
                using var claimContext = CreateDbContext(connectionString);
                var repository = new PostgresReconciliationHistoryRepository(
                    claimContext,
                    TimeProvider.System);
                if (repository.TryClaimJob(
                    batchId,
                    ReconciliationInputType.DatabaseSources,
                    owner,
                    now,
                    TimeSpan.FromMinutes(2)))
                {
                    Interlocked.Increment(ref successfulClaims);
                }
            });

            Assert.Equal(1, successfulClaims);
            using var readContext = CreateDbContext(connectionString);
            var stored = new PostgresReconciliationHistoryRepository(
                readContext,
                TimeProvider.System).GetById(batchId);
            Assert.NotNull(stored);
            Assert.Equal(ReconciliationBatchStatus.Processing, stored.Status);
            Assert.Equal(1, stored.AttemptCount);
            Assert.NotNull(stored.LeaseOwner);
            Assert.NotNull(stored.LeaseExpiresAt);
        }
        finally
        {
            using var cleanupContext = CreateDbContext(connectionString);
            var storedEntity = cleanupContext.ReconciliationBatches.Find(batchId);
            if (storedEntity is not null)
            {
                cleanupContext.ReconciliationBatches.Remove(storedEntity);
                cleanupContext.SaveChanges();
            }
        }
    }

    [Fact]
    public void UploadedFileJob_EnforcesPostgresTemporaryStorageAffinity_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var owningStorageKey = Guid.NewGuid().ToString("N");
        var otherStorageKey = Guid.NewGuid().ToString("N");
        Guid batchId;
        using (var setupContext = CreateDbContext(connectionString))
        {
            setupContext.Database.Migrate();
            var setupRepository = new PostgresReconciliationHistoryRepository(
                setupContext,
                TimeProvider.System);
            batchId = setupRepository.AddQueued(
                "branch.csv",
                "bank.csv",
                ReconciliationInputType.UploadedFiles,
                temporaryStorageKey: owningStorageKey).Id;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            using (var otherContext = CreateDbContext(connectionString))
            {
                var otherRepository = new PostgresReconciliationHistoryRepository(
                    otherContext,
                    TimeProvider.System);
                Assert.Empty(otherRepository.GetActiveUploadedFileJobIds(
                    otherStorageKey,
                    [batchId]));
                Assert.DoesNotContain(
                    batchId,
                    otherRepository.GetClaimableJobIds(
                        ReconciliationInputType.UploadedFiles,
                        now,
                        take: 10,
                        temporaryStorageKey: otherStorageKey));
                Assert.False(otherRepository.TryClaimJob(
                    batchId,
                    ReconciliationInputType.UploadedFiles,
                    "other-storage-worker",
                    now,
                    TimeSpan.FromMinutes(2),
                    otherStorageKey));
            }

            using var ownerContext = CreateDbContext(connectionString);
            var ownerRepository = new PostgresReconciliationHistoryRepository(
                ownerContext,
                TimeProvider.System);
            Assert.Contains(
                batchId,
                ownerRepository.GetActiveUploadedFileJobIds(
                    owningStorageKey,
                    [batchId]));
            Assert.Contains(
                batchId,
                ownerRepository.GetClaimableJobIds(
                    ReconciliationInputType.UploadedFiles,
                    now,
                    take: 10,
                    temporaryStorageKey: owningStorageKey));
            Assert.True(ownerRepository.TryClaimJob(
                batchId,
                ReconciliationInputType.UploadedFiles,
                "owning-storage-worker",
                now,
                TimeSpan.FromMinutes(2),
                owningStorageKey));
        }
        finally
        {
            using var cleanupContext = CreateDbContext(connectionString);
            var storedEntity = cleanupContext.ReconciliationBatches.Find(batchId);
            if (storedEntity is not null)
            {
                cleanupContext.ReconciliationBatches.Remove(storedEntity);
                cleanupContext.SaveChanges();
            }
        }
    }

    [Fact]
    public void SettingsRepositories_RoundTripThroughPostgres_WhenConnectionStringIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        using var dbContext = CreateDbContext(connectionString);
        dbContext.Database.Migrate();
        using var transaction = dbContext.Database.BeginTransaction();
        var fileSchemaRepository = new PostgresReconciliationFileSchemaRepository(
            dbContext,
            TimeProvider.System);
        var comparisonOptionsRepository = new PostgresReconciliationComparisonOptionsRepository(
            dbContext,
            TimeProvider.System);
        var schema = new ReconciliationFileSchemaOptions
        {
            Columns = ReconciliationFileSchemaOptions.GetDefaultColumns()
        };
        schema.Columns[0].Name = "SubeKodu";
        var comparisonOptions = new ReconciliationComparisonOptions
        {
            MatchingFields = ["BranchCode", "TransactionNumber"],
            ComparisonFields = ["Amount"],
            ResultFields = ["BranchCode", "TransactionNumber"]
        };

        fileSchemaRepository.Save(schema);
        comparisonOptionsRepository.Save(comparisonOptions);
        dbContext.ChangeTracker.Clear();

        Assert.Equal("SubeKodu", fileSchemaRepository.Get()!.Columns[0].Name);
        Assert.Equal(
            ["BranchCode", "TransactionNumber"],
            comparisonOptionsRepository.Get()!.MatchingFields);

        transaction.Rollback();
    }

    [Fact]
    public async Task AuditRetention_AtomicallyArchivesAndQueriesEvents_WhenPostgresIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var oldTime = DateTimeOffset.UtcNow.AddDays(-400);
        var timeProvider = new MutableTimeProvider(oldTime);
        Guid auditId;
        using (var setupContext = CreateDbContext(connectionString))
        {
            setupContext.Database.Migrate();
            var repository = new PostgresReconciliationAuditRepository(setupContext, timeProvider);
            auditId = repository.Add(
                ReconciliationAuditAction.SourceUpdated,
                "postgres-retention-test",
                ReconciliationAuditResourceType.ReconciliationSource,
                Guid.NewGuid().ToString("N"),
                beforeState: null,
                afterState: new { Enabled = true }).Id;
        }

        try
        {
            timeProvider.UtcNow = DateTimeOffset.UtcNow;
            using var archiveContext = CreateDbContext(connectionString);
            var repository = new PostgresReconciliationAuditRepository(
                archiveContext,
                timeProvider);

            var result = await repository.ArchiveAndPurgeAsync(
                timeProvider.UtcNow.AddDays(-365),
                timeProvider.UtcNow.AddDays(-2555),
                batchSize: 100);
            archiveContext.ChangeTracker.Clear();

            Assert.Equal(1, result.ArchivedCount);
            Assert.Null(await archiveContext.ReconciliationAuditEvents.FindAsync(auditId));
            Assert.NotNull(await archiveContext.ReconciliationAuditEventArchives.FindAsync(auditId));
            var stored = Assert.Single(repository.GetAll(
                new ReconciliationAuditQuery { Actor = "postgres-retention-test" }));
            Assert.NotNull(stored.ArchivedAt);
            Assert.Matches("^[a-f0-9]{64}$", stored.IntegrityHash);
        }
        finally
        {
            using var cleanupContext = CreateDbContext(connectionString);
            var active = cleanupContext.ReconciliationAuditEvents.Find(auditId);
            var archived = cleanupContext.ReconciliationAuditEventArchives.Find(auditId);
            if (active is not null)
            {
                cleanupContext.ReconciliationAuditEvents.Remove(active);
            }
            if (archived is not null)
            {
                cleanupContext.ReconciliationAuditEventArchives.Remove(archived);
            }
            cleanupContext.SaveChanges();
        }
    }

    private static ReconciliationDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ReconciliationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ReconciliationDbContext(options);
    }

    private static ReconciliationSummary CreateSummary()
    {
        return new ReconciliationSummary
        {
            TotalBranchRecords = 2,
            TotalBankRecords = 2,
            MatchedCount = 1,
            MismatchCount = 1,
            Results =
            [
                new ReconciliationResult
                {
                    Status = ReconciliationStatus.Matched,
                    BranchCode = "BEYLIKDUZU",
                    FundCode = "A",
                    TransactionNumber = "TX001",
                    BranchRecord = CreateRecord("TX001", quantity: 100),
                    BankRecord = CreateRecord("TX001", quantity: 100),
                    QuantityDifference = 0,
                    AmountDifference = 0
                },
                new ReconciliationResult
                {
                    Status = ReconciliationStatus.QuantityMismatch,
                    BranchCode = "BEYLIKDUZU",
                    FundCode = "B",
                    TransactionNumber = "TX002",
                    BranchRecord = CreateRecord("TX002", fundCode: "B", quantity: 50),
                    BankRecord = CreateRecord("TX002", fundCode: "B", quantity: 45),
                    QuantityDifference = 5,
                    AmountDifference = 0,
                    FieldDifferences = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Commission"] = 2.34m
                    }
                }
            ]
        };
    }

    private static TransactionRecord CreateRecord(
        string transactionNumber,
        string fundCode = "A",
        decimal quantity = 100)
    {
        return new TransactionRecord
        {
            BranchCode = "BEYLIKDUZU",
            FundCode = fundCode,
            TransactionNumber = transactionNumber,
            TransactionDate = new DateOnly(2026, 6, 26),
            Quantity = quantity,
            Amount = 10000,
            ExtraFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Commission"] = quantity == 50 ? "12.34" : "10.00"
            }
        };
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
