using System.Net;
using BankingReconciliation.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BankingReconciliation.Tests;

public class ReconciliationReadinessEndpointTests
{
    [Fact]
    public async Task Readiness_ReturnsServiceUnavailableWithoutLeakingFailureDetails()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IReconciliationTemporaryFileStore>();
                    services.AddSingleton<
                        IReconciliationTemporaryFileStore,
                        UnavailableTemporaryFileStore>();
                });
            });
        using var client = factory.CreateClient();

        using var readinessResponse = await client.GetAsync("/api/health/ready");
        var readinessJson = await readinessResponse.Content.ReadAsStringAsync();
        using var livenessResponse = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readinessResponse.StatusCode);
        Assert.Contains("\"status\":\"NotReady\"", readinessJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "\"temporaryStorage\":\"Unavailable\"",
            readinessJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-storage-detail", readinessJson, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, livenessResponse.StatusCode);
    }

    [Fact]
    public async Task AuditRetentionHealth_ReturnsServiceUnavailableAfterFailedRun()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var monitor = factory.Services.GetRequiredService<ReconciliationAuditRetentionMonitor>();
        var now = DateTimeOffset.UtcNow;
        monitor.MarkStarted(now);
        monitor.MarkFailed(now.AddSeconds(1));

        using var response = await client.GetAsync("/api/health/audit-retention");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("\"status\":\"Degraded\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastRunFailed", json, StringComparison.Ordinal);
        Assert.DoesNotContain("exception", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UnavailableTemporaryFileStore : IReconciliationTemporaryFileStore
    {
        public string StorageKey => Guid.Empty.ToString("N");

        public Task VerifyAvailabilityAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("sensitive-storage-detail");

        public Task SaveAsync(
            Guid batchId,
            IFormFile branchFile,
            IFormFile bankFile,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> SaveBranchStreamAsync(
            Guid batchId,
            Stream source,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> SaveBankStreamAsync(
            Guid batchId,
            Stream source,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenBranchReadAsync(
            Guid batchId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenBankReadAsync(
            Guid batchId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(
            Guid batchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyCollection<Guid>> GetExpiredBatchIdsAsync(
            DateTimeOffset olderThan,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Guid>>([]);

        public Task<bool> DeleteAsync(
            Guid batchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
