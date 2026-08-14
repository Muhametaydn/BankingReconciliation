using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BankingReconciliation.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BankingReconciliation.Tests;

public sealed class LocalAuthenticationEndpointsTests
{
    [Fact]
    public async Task RegisterLoginAndRoleManagement_EnforceAdministratorBootstrap()
    {
        await using var factory = new LocalAuthenticationWebApplicationFactory();
        using var client = factory.CreateClient();

        var administrator = await Register(client, "admin.user", "StrongPass123");
        Assert.True(administrator.IsFirstAdministrator);
        Assert.Equal("Administrator", administrator.User.Role);

        var operatorSession = await Register(client, "operator.user", "OperatorPass123");
        Assert.False(operatorSession.IsFirstAdministrator);
        Assert.Equal("Operator", operatorSession.User.Role);

        using var listRequest = AuthorizedRequest(HttpMethod.Get, "/api/auth/users", administrator.AccessToken);
        using var listResponse = await client.SendAsync(listRequest);
        var users = await listResponse.Content.ReadFromJsonAsync<List<LocalUserResponse>>();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(2, users?.Count);

        using var roleRequest = AuthorizedRequest(
            HttpMethod.Put,
            $"/api/auth/users/{operatorSession.User.Id}/role",
            administrator.AccessToken,
            new UpdateLocalUserRoleRequest { Role = "Approver" });
        using var roleResponse = await client.SendAsync(roleRequest);
        var approver = await roleResponse.Content.ReadFromJsonAsync<LocalUserResponse>();
        Assert.Equal(HttpStatusCode.OK, roleResponse.StatusCode);
        Assert.Equal("Approver", approver?.Role);

        using var staleSessionRequest = AuthorizedRequest(
            HttpMethod.Get,
            "/api/auth/session",
            operatorSession.AccessToken);
        using var staleSessionResponse = await client.SendAsync(staleSessionRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, staleSessionResponse.StatusCode);

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginLocalUserRequest { Username = "operator.user", Password = "OperatorPass123" });
        var renewedSession = await loginResponse.Content.ReadFromJsonAsync<LocalAuthenticationSessionResponse>();
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Equal("Approver", renewedSession?.User.Role);

        using var selfRoleRequest = AuthorizedRequest(
            HttpMethod.Put,
            $"/api/auth/users/{administrator.User.Id}/role",
            administrator.AccessToken,
            new UpdateLocalUserRoleRequest { Role = "Operator" });
        using var selfRoleResponse = await client.SendAsync(selfRoleRequest);
        Assert.Equal(HttpStatusCode.Conflict, selfRoleResponse.StatusCode);
    }

    [Fact]
    public async Task FileSchemaChanges_RequireAdministratorRole()
    {
        await using var factory = new LocalAuthenticationWebApplicationFactory();
        using var client = factory.CreateClient();

        var administrator = await Register(client, "schema.admin", "StrongPass123");
        var operatorSession = await Register(client, "schema.operator", "OperatorPass123");

        using var roleRequest = AuthorizedRequest(
            HttpMethod.Put,
            $"/api/auth/users/{operatorSession.User.Id}/role",
            administrator.AccessToken,
            new UpdateLocalUserRoleRequest { Role = "Approver" });
        using var roleResponse = await client.SendAsync(roleRequest);
        Assert.Equal(HttpStatusCode.OK, roleResponse.StatusCode);

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginLocalUserRequest { Username = "schema.operator", Password = "OperatorPass123" });
        var approver = await loginResponse.Content.ReadFromJsonAsync<LocalAuthenticationSessionResponse>();
        Assert.NotNull(approver);

        using var schemaResponse = await client.GetAsync("/api/reconciliation-file-schema");
        var schemaJson = await schemaResponse.Content.ReadAsStringAsync();
        var schemaPayload = $"{{\"columns\":{schemaJson}}}";
        Assert.Equal(HttpStatusCode.OK, schemaResponse.StatusCode);

        using var approverRequest = AuthorizedJsonRequest(
            HttpMethod.Put,
            "/api/reconciliation-file-schema",
            approver!.AccessToken,
            schemaPayload);
        using var approverResponse = await client.SendAsync(approverRequest);
        Assert.Equal(HttpStatusCode.Forbidden, approverResponse.StatusCode);

        using var administratorRequest = AuthorizedJsonRequest(
            HttpMethod.Put,
            "/api/reconciliation-file-schema",
            administrator.AccessToken,
            schemaPayload);
        using var administratorResponse = await client.SendAsync(administratorRequest);
        Assert.Equal(HttpStatusCode.OK, administratorResponse.StatusCode);
    }

    [Fact]
    public async Task FileComparison_UsesAuthenticatedUserIdentity()
    {
        await using var factory = new LocalAuthenticationWebApplicationFactory();
        using var client = factory.CreateClient();

        _ = await Register(client, "upload.admin", "StrongPass123");
        var operatorSession = await Register(client, "upload.operator", "OperatorPass123");
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/api/reconciliations/compare",
            operatorSession.AccessToken);
        request.Content = CreateComparisonContent();
        using var response = await client.SendAsync(request);
        var resultJson = await response.Content.ReadAsStringAsync();
        using var result = JsonDocument.Parse(resultJson);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("upload.operator", result.RootElement.GetProperty("initiatedBy").GetString());
        Assert.Equal(2, result.RootElement.GetProperty("matchedCount").GetInt32());
    }

    private static async Task<LocalAuthenticationSessionResponse> Register(
        HttpClient client,
        string username,
        string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterLocalUserRequest { Username = username, Password = password });
        var session = await response.Content.ReadFromJsonAsync<LocalAuthenticationSessionResponse>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<LocalAuthenticationSessionResponse>(session);
    }

    private static HttpRequestMessage AuthorizedRequest<T>(
        HttpMethod method,
        string path,
        string token,
        T? body = default)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string path,
        string token) => AuthorizedRequest<object>(method, path, token);

    private static HttpRequestMessage AuthorizedJsonRequest(
        HttpMethod method,
        string path,
        string token,
        string json)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return request;
    }

    private static MultipartFormDataContent CreateComparisonContent()
    {
        const string csv =
            "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount\n" +
            "B001,F001,T001,2026-08-14,10,100\n" +
            "B001,F001,T002,2026-08-14,20,200\n";
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(csv), "branchFile", "branch.csv");
        content.Add(new StringContent(csv), "bankFile", "bank.csv");
        return content;
    }

    private sealed class LocalAuthenticationWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration(configurationBuilder =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ReconciliationDatabase"] = string.Empty,
                    ["ReconciliationJobs:PollIntervalMilliseconds"] = "100",
                    ["ReconciliationJobs:RetryDelaySeconds"] = "0"
                });
            });
        }
    }
}
