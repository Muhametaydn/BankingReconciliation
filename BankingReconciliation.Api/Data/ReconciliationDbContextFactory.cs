using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BankingReconciliation.Api.Data;

public class ReconciliationDbContextFactory : IDesignTimeDbContextFactory<ReconciliationDbContext>
{
    public ReconciliationDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
        {
            basePath = Path.Combine(basePath, "BankingReconciliation.Api");
        }

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("ReconciliationDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:ReconciliationDatabase must be configured for EF Core migrations.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<ReconciliationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new ReconciliationDbContext(optionsBuilder.Options);
    }
}
