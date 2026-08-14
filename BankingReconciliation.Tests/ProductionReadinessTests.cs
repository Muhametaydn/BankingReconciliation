using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using BankingReconciliation.Api.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BankingReconciliation.Tests;

public class ProductionReadinessTests
{
    [Fact]
    public void ProductionValidator_RejectsUnsafeDefaultsWithoutEchoingSecrets()
    {
        var errors = ReconciliationProductionReadinessValidator.Validate(
            "Production",
            "Host=db;Database=reconciliation;Username=app;Password=root",
            "*",
            new ReconciliationAuthenticationOptions(),
            new ReconciliationUploadOptions(),
            new ReconciliationObservabilityOptions(),
            new ReconciliationProductionOptions());
        var message = string.Join(" ", errors);

        Assert.Contains("unsafe placeholder password", message);
        Assert.Contains("Authentication:Authority", message);
        Assert.Contains("Authentication:Audience", message);
        Assert.Contains("AllowedHosts", message);
        Assert.Contains("persistent shared or object storage", message);
        Assert.Contains("OpenTelemetryEnabled", message);
        Assert.Contains("DeploymentVersion", message);
        Assert.Contains("KnownProxyNetworks", message);
        Assert.DoesNotContain("Password=root", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Prefer")]
    [InlineData("Require")]
    public void ProductionValidator_RejectsDatabaseConnectionsWithoutFullTlsVerification(string sslMode)
    {
        var errors = ReconciliationProductionReadinessValidator.Validate(
            "Production",
            $"Host=db;Database=reconciliation;Username=app;Password=strong-random-value;SSL Mode={sslMode}",
            "reconciliation.example.com",
            new ReconciliationAuthenticationOptions
            {
                Authority = "https://identity.example.com",
                Audience = "banking-reconciliation-api"
            },
            new ReconciliationUploadOptions
            {
                TemporaryStorageMode = ReconciliationTemporaryStorageMode.SharedFileSystem,
                TemporaryStoragePath = "/data/uploads"
            },
            new ReconciliationObservabilityOptions
            {
                OpenTelemetryEnabled = true,
                OtlpEndpoint = "http://otel-collector:4317"
            },
            new ReconciliationProductionOptions
            {
                DeploymentVersion = "2026.08.10.1",
                KnownProxyNetworks = ["10.0.0.0/8"]
            });

        Assert.Contains(
            errors,
            error => error.Contains("SSL Mode=VerifyFull", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionValidator_AcceptsHardenedExternalConfiguration()
    {
        var errors = ReconciliationProductionReadinessValidator.Validate(
            "Staging",
            "Host=postgres;Database=reconciliation;Username=app;Password=strong-random-value;SSL Mode=VerifyFull",
            "reconciliation.example.com",
            new ReconciliationAuthenticationOptions
            {
                Authority = "https://identity.example.com",
                Audience = "banking-reconciliation-api"
            },
            new ReconciliationUploadOptions
            {
                TemporaryStorageMode = ReconciliationTemporaryStorageMode.SharedFileSystem,
                TemporaryStoragePath = "/data/uploads"
            },
            new ReconciliationObservabilityOptions
            {
                OpenTelemetryEnabled = true,
                OtlpEndpoint = "http://otel-collector:4317"
            },
            new ReconciliationProductionOptions
            {
                DeploymentVersion = "2026.08.10.1",
                KnownProxyNetworks = ["10.0.0.0/8"]
            });

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("2001:db8::/32", true)]
    [InlineData("10.0.0.0/99", false)]
    [InlineData("not-a-network", false)]
    public void RuntimeOptions_ValidateTrustedProxyNetworks(string network, bool expected)
    {
        var options = new ReconciliationProductionOptions
        {
            KnownProxyNetworks = [network]
        };

        Assert.Equal(
            expected,
            ReconciliationProductionReadinessValidator.HasValidRuntimeOptions(options));
    }

    [Fact]
    public async Task Responses_IncludeSecurityHeaders()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task BaseSettings_DoNotContainCommittedDatabasePassword()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var developmentSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        var json = await File.ReadAllTextAsync(settingsPath);
        var developmentJson = await File.ReadAllTextAsync(developmentSettingsPath);
        using var document = JsonDocument.Parse(json);
        using var developmentDocument = JsonDocument.Parse(developmentJson);

        Assert.Equal(
            string.Empty,
            document.RootElement
                .GetProperty("ConnectionStrings")
                .GetProperty("ReconciliationDatabase")
                .GetString());
        Assert.DoesNotContain("Password=", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            string.Empty,
            developmentDocument.RootElement
                .GetProperty("ConnectionStrings")
                .GetProperty("ReconciliationDatabase")
                .GetString());
        Assert.DoesNotContain("Password=", developmentJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GlobalRateLimit_ReturnsSanitizedTooManyRequestsResponse()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.PostConfigure<RateLimiterOptions>(options =>
                {
                    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                        RateLimitPartition.GetFixedWindowLimiter(
                            "test-client",
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 1,
                                Window = TimeSpan.FromMinutes(1),
                                QueueLimit = 0,
                                AutoReplenishment = true
                            }));
                })));
        using var client = factory.CreateClient();

        using var accepted = await client.GetAsync("/api/health");
        using var rejected = await client.GetAsync("/api/health");
        var json = await rejected.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("RateLimitExceeded", json);
        Assert.DoesNotContain("127.0.0.1", json);
    }
}
