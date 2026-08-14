using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class ReconciliationReadinessServiceTests
{
    private const string ConnectionStringEnvironmentVariable =
        "BANKING_RECONCILIATION_POSTGRES_TEST_CONNECTION";

    [Fact]
    public async Task CheckAsync_VerifiesPostgresAndTemporaryStorage_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "BankingReconciliation.Tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            using var services = new ServiceCollection()
                .AddDbContext<ReconciliationDbContext>(options =>
                    options.UseNpgsql(connectionString))
                .BuildServiceProvider();
            var temporaryFileStore = new ReconciliationTemporaryFileStore(
                Options.Create(new ReconciliationUploadOptions
                {
                    TemporaryStoragePath = rootPath
                }));
            var readinessService = new ReconciliationReadinessService(
                temporaryFileStore,
                services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new ReconciliationReadinessOptions
                {
                    TimeoutSeconds = 5
                }),
                NullLogger<ReconciliationReadinessService>.Instance);

            var result = await readinessService.CheckAsync();

            Assert.True(result.IsReady);
            Assert.True(result.DatabaseAvailable);
            Assert.True(result.TemporaryStorageAvailable);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
