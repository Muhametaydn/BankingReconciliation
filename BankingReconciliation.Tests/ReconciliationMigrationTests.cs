using BankingReconciliation.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BankingReconciliation.Tests;

public class ReconciliationMigrationTests
{
    [Fact]
    public void MigrationsAssembly_DiscoversPersistentSettingsMigrations()
    {
        var options = new DbContextOptionsBuilder<ReconciliationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var dbContext = new ReconciliationDbContext(options);
        var migrationIds = dbContext.GetService<IMigrationsAssembly>().Migrations.Keys;

        Assert.Contains("20260716120000_AddDynamicDifferenceJson", migrationIds);
        Assert.Contains("20260716143000_AddPersistentFileSchemaSettings", migrationIds);
        Assert.Contains("20260716150000_AddPersistentComparisonSettings", migrationIds);
        Assert.Contains("20260722101558_AddReconciliationInputType", migrationIds);
        Assert.Contains("20260722105646_AddReconciliationApprovalWorkflow", migrationIds);
        Assert.Contains("20260722121222_AddReconciliationAuditTrail", migrationIds);
        Assert.Contains("20260722145742_AddPersistentJobLeasesAndRetries", migrationIds);
        Assert.Contains("20260808141822_AddAuditRetentionArchive", migrationIds);
        Assert.Contains("20260808150150_AddImmutableAuditArchiveTracking", migrationIds);
    }
}
