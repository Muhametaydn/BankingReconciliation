using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BankingReconciliation.Tests;

public class ReconciliationEndpointTests : IClassFixture<BankingReconciliationWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly BankingReconciliationWebApplicationFactory _factory;

    public ReconciliationEndpointTests(BankingReconciliationWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHistory_ReturnsBadRequest_WhenPagingOrDateRangeIsInvalid()
    {
        using var pagingResponse = await _client.GetAsync("/api/reconciliations?take=201");
        using var dateResponse = await _client.GetAsync(
            "/api/reconciliations?from=2026-07-11T00:00:00Z&to=2026-07-10T00:00:00Z");
        using var searchResponse = await _client.GetAsync(
            $"/api/reconciliations?search={new string('x', 201)}");

        Assert.Equal(HttpStatusCode.BadRequest, pagingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, dateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, searchResponse.StatusCode);
    }

    [Fact]
    public async Task GetHistory_ReturnsTotalCountHeader()
    {
        using var response = await _client.GetAsync("/api/reconciliations?take=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Total-Count", out var values));
        Assert.True(int.TryParse(Assert.Single(values), out var totalCount));
        Assert.True(totalCount >= 0);
    }

    [Fact]
    public async Task Compare_ReturnsSummary_WhenFilesAreValid()
    {
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                BEYLIKDUZU,B,TX002,2026-06-26,50,5000
                BEYLIKDUZU,C,TX003,2026-06-26,20,2000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                BEYLIKDUZU,B,TX002,2026-06-26,45,5000
                BEYLIKDUZU,D,TX004,2026-06-26,10,1000
                """);

        using var response = await _client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, root.GetProperty("totalBranchRecords").GetInt32());
        Assert.Equal(3, root.GetProperty("totalBankRecords").GetInt32());
        Assert.Equal(1, root.GetProperty("matchedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("mismatchCount").GetInt32());
        Assert.Equal(1, root.GetProperty("onlyInBranchCount").GetInt32());
        Assert.Equal(1, root.GetProperty("onlyInBankCount").GetInt32());
        Assert.NotEqual(Guid.Empty, root.GetProperty("batchId").GetGuid());
        Assert.Equal("Completed", root.GetProperty("batchStatus").GetString());
        Assert.Equal("branch-transactions.csv", root.GetProperty("branchFileName").GetString());
        Assert.Equal("bank-transactions.csv", root.GetProperty("bankFileName").GetString());
        Assert.True(root.GetProperty("processingDurationMilliseconds").GetInt64() >= 0);

        var results = root.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(4, results.Length);
        Assert.Contains(results, result =>
            result.GetProperty("status").GetString() == "Matched" &&
            result.GetProperty("transactionNumber").GetString() == "TX001");
        Assert.Contains(results, result =>
            result.GetProperty("status").GetString() == "QuantityMismatch" &&
            result.GetProperty("transactionNumber").GetString() == "TX002");

        var matchedResult = results.Single(result =>
            result.GetProperty("status").GetString() == "Matched");
        var branchRecord = matchedResult.GetProperty("branchRecord");
        Assert.False(branchRecord.TryGetProperty("matchingKey", out _));
    }

    [Fact]
    public async Task DecideApproval_ReturnsUnauthorized_WhenUserIsNotAuthenticated()
    {
        var batchId = await CreateCompletedBatchAsync();
        using var request = CreateApprovalRequest(batchId, "Approve");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DecideApproval_ReturnsForbidden_WhenUserDoesNotHaveApprovalPermission()
    {
        var batchId = await CreateCompletedBatchAsync();
        using var request = CreateApprovalRequest(batchId, "Approve");
        request.Headers.Add("X-Test-Actor", "reviewer-1");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DecideApproval_ApprovesCompletedBatch_AndRejectsSecondDecision()
    {
        var batchId = await CreateCompletedBatchAsync();
        using var request = CreateApprovalRequest(batchId, "Approve", "Kontrol edildi.");
        request.Headers.Add("X-Test-Actor", "reviewer-2");
        request.Headers.Add("X-Test-Permission", "reconciliation.approve");

        using var response = await _client.SendAsync(request);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Approved", body.RootElement.GetProperty("approvalStatus").GetString());
        Assert.Equal("reviewer-2", body.RootElement.GetProperty("decisionBy").GetString());
        Assert.Equal("Kontrol edildi.", body.RootElement.GetProperty("decisionComment").GetString());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("decisionAt").ValueKind);

        using var secondRequest = CreateApprovalRequest(batchId, "Reject", "Tekrar karar.");
        secondRequest.Headers.Add("X-Test-Actor", "reviewer-2");
        secondRequest.Headers.Add("X-Test-Permission", "reconciliation.approve");
        using var secondResponse = await _client.SendAsync(secondRequest);
        using var secondBody = await JsonDocument.ParseAsync(
            await secondResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal(
            "ApprovalAlreadyDecided",
            secondBody.RootElement.GetProperty("error").GetString());

        using var auditResponse = await _client.GetAsync(
            "/api/reconciliation-audit-events?action=ReconciliationApproved&actor=reviewer-2");
        using var auditBody = await JsonDocument.ParseAsync(
            await auditResponse.Content.ReadAsStreamAsync());
        var auditEvent = Assert.Single(auditBody.RootElement.EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        Assert.Equal(batchId.ToString(), auditEvent.GetProperty("resourceId").GetString());
        Assert.Equal("Approved", auditEvent
            .GetProperty("afterState")
            .GetProperty("approvalStatus")
            .GetString());
    }

    [Fact]
    public async Task DecideApproval_RequiresComment_WhenRejecting()
    {
        var batchId = await CreateCompletedBatchAsync();
        using var request = CreateApprovalRequest(batchId, "Reject");
        request.Headers.Add("X-Test-Actor", "reviewer-3");
        request.Headers.Add("X-Test-Role", "ReconciliationApprover");

        using var response = await _client.SendAsync(request);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "RejectionCommentRequired",
            body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DecideApproval_RejectsDecisionByTheInitiatingUser()
    {
        var batchId = await CreateCompletedBatchAsync();
        using var request = CreateApprovalRequest(batchId, "Approve");
        request.Headers.Add("X-Test-Actor", "test-operator");
        request.Headers.Add("X-Test-Permission", "reconciliation.approve");

        using var response = await _client.SendAsync(request);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SelfApprovalNotAllowed", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DecideApproval_ReturnsConflict_WhenBatchIsNotCompleted()
    {
        Guid batchId;
        using (var scope = _factory.Services.CreateScope())
        {
            batchId = scope.ServiceProvider
                .GetRequiredService<IReconciliationHistoryRepository>()
                .AddQueued(
                    "branch.csv",
                    "bank.csv",
                    temporaryStorageKey: Guid.NewGuid().ToString("N"))
                .Id;
        }

        using var request = CreateApprovalRequest(batchId, "Approve");
        request.Headers.Add("X-Test-Actor", "reviewer-4");
        request.Headers.Add("X-Test-Permission", "reconciliation.approve");

        using var response = await _client.SendAsync(request);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("BatchNotCompleted", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CompareDatabaseSources_ReturnsSummaryAndStoresBatch()
    {
        var databaseReader = new StubDatabaseSourceReader(new Dictionary<string, IReadOnlyList<TransactionRecord>>
        {
            ["BRANCH"] =
            [
                CreateDatabaseRecord("TX001", amount: 10000),
                CreateDatabaseRecord("TX002", amount: 5000)
            ],
            ["BANK"] =
            [
                CreateDatabaseRecord("TX001", amount: 10000),
                CreateDatabaseRecord("TX002", amount: 4990)
            ]
        });
        await using var factory = CreateFactoryWithDatabaseReader(databaseReader);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/reconciliations/compare-database-sources",
            content: null);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, root.GetProperty("matchedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("mismatchCount").GetInt32());
        Assert.Equal("database:BRANCH", root.GetProperty("branchFileName").GetString());
        Assert.Equal("database:BANK", root.GetProperty("bankFileName").GetString());

        using var historyResponse = await client.GetAsync("/api/reconciliations");
        using var historyBody = await JsonDocument.ParseAsync(await historyResponse.Content.ReadAsStreamAsync());
        Assert.Contains(historyBody.RootElement.EnumerateArray(), batch =>
            batch.GetProperty("id").GetGuid() == root.GetProperty("batchId").GetGuid());
    }

    [Fact]
    public async Task QueueDatabaseSourcesComparison_CompletesBatchInBackground()
    {
        var databaseReader = new StubDatabaseSourceReader(new Dictionary<string, IReadOnlyList<TransactionRecord>>
        {
            ["BRANCH"] = [CreateDatabaseRecord("TX001", amount: 10000)],
            ["BANK"] = [CreateDatabaseRecord("TX001", amount: 9990)]
        });
        await using var factory = CreateFactoryWithDatabaseReader(databaseReader);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/reconciliations/compare-database-sources/jobs",
            content: null);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var batchId = body.RootElement.GetProperty("batchId").GetGuid();
        using var completedBatch = await WaitForBatchStatusAsync(client, batchId, "Completed");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Queued", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, completedBatch.RootElement.GetProperty("mismatchCount").GetInt32());
        Assert.Equal(1, completedBatch.RootElement.GetProperty("attemptCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, completedBatch.RootElement.GetProperty("leaseExpiresAt").ValueKind);
    }

    [Fact]
    public async Task QueueDatabaseSourcesComparison_StoresFailureOnSameBatch()
    {
        var databaseReader = new StubDatabaseSourceReader(
            new Dictionary<string, IReadOnlyList<TransactionRecord>>(),
            failingSourceCode: "BANK");
        await using var factory = CreateFactoryWithDatabaseReader(databaseReader);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/reconciliations/compare-database-sources/jobs",
            content: null);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var batchId = body.RootElement.GetProperty("batchId").GetGuid();
        using var failedBatch = await WaitForBatchStatusAsync(client, batchId, "Failed");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("DatabaseSourceReadFailed", failedBatch.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(3, failedBatch.RootElement.GetProperty("attemptCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, failedBatch.RootElement.GetProperty("nextAttemptAt").ValueKind);
    }

    [Fact]
    public async Task QueueFilesComparison_CompletesBatchAndDeletesTemporaryFiles()
    {
        var temporaryStoragePath = CreateTemporaryStoragePath();
        try
        {
            await using var factory = CreateFactoryWithTemporaryStorage(temporaryStoragePath);
            using var client = factory.CreateClient();
            using var content = CreateMultipartContent(
                branchCsv: """
                    BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                    BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                    """,
                bankCsv: """
                    BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                    BEYLIKDUZU,A,TX001,2026-06-26,100,9990
                    """,
                branchFileName: "../branch-transactions.csv",
                bankFileName: "..\\bank-transactions.csv");

            using var response = await client.PostAsync("/api/reconciliations/compare/jobs", content);
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var batchId = body.RootElement.GetProperty("batchId").GetGuid();
            using var completedBatch = await WaitForBatchStatusAsync(client, batchId, "Completed");
            var temporaryFileStore = factory.Services.GetRequiredService<IReconciliationTemporaryFileStore>();
            await WaitForTemporaryFilesToBeDeletedAsync(temporaryFileStore, batchId);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("Queued", body.RootElement.GetProperty("status").GetString());
            Assert.Equal("UploadedFiles", body.RootElement.GetProperty("inputType").GetString());
            Assert.Equal("UploadedFiles", completedBatch.RootElement.GetProperty("inputType").GetString());
            Assert.Equal("branch-transactions.csv", completedBatch.RootElement.GetProperty("branchFileName").GetString());
            Assert.Equal("bank-transactions.csv", completedBatch.RootElement.GetProperty("bankFileName").GetString());
            Assert.Equal(1, completedBatch.RootElement.GetProperty("mismatchCount").GetInt32());
            Assert.False(await temporaryFileStore.ExistsAsync(batchId));
        }
        finally
        {
            DeleteTemporaryStoragePath(temporaryStoragePath);
        }
    }

    [Fact]
    public async Task QueueFilesComparison_StoresParseFailureAndDeletesTemporaryFiles()
    {
        var temporaryStoragePath = CreateTemporaryStoragePath();
        try
        {
            await using var factory = CreateFactoryWithTemporaryStorage(temporaryStoragePath);
            using var client = factory.CreateClient();
            using var content = CreateMultipartContent(
                branchCsv: """
                    BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                    BEYLIKDUZU,A,TX001,2026-06-26,100,not-a-number
                    """,
                bankCsv: """
                    BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                    BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                    """);

            using var response = await client.PostAsync("/api/reconciliations/compare/jobs", content);
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var batchId = body.RootElement.GetProperty("batchId").GetGuid();
            using var failedBatch = await WaitForBatchStatusAsync(client, batchId, "Failed");
            var temporaryFileStore = factory.Services.GetRequiredService<IReconciliationTemporaryFileStore>();
            await WaitForTemporaryFilesToBeDeletedAsync(temporaryFileStore, batchId);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("InvalidCsvFile", failedBatch.RootElement.GetProperty("errorCode").GetString());
            Assert.False(await temporaryFileStore.ExistsAsync(batchId));
        }
        finally
        {
            DeleteTemporaryStoragePath(temporaryStoragePath);
        }
    }

    [Fact]
    public async Task QueueFilesComparison_ReturnsBadRequestAndCleansFiles_WhenBankFileIsMissing()
    {
        var temporaryStoragePath = CreateTemporaryStoragePath();
        try
        {
            await using var factory = CreateFactoryWithTemporaryStorage(temporaryStoragePath);
            using var client = factory.CreateClient();
            using var content = new MultipartFormDataContent();
            content.Add(CreateCsvContent("branch-content"), "branchFile", "branch.csv");

            using var response = await client.PostAsync("/api/reconciliations/compare/jobs", content);
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("MissingBankFile", body.RootElement.GetProperty("error").GetString());
            Assert.False(HasTemporaryBatchDirectories(temporaryStoragePath));
        }
        finally
        {
            DeleteTemporaryStoragePath(temporaryStoragePath);
        }
    }

    [Fact]
    public async Task QueueFilesComparison_RejectsActualStreamBytesOverLimit_AndCleansFiles()
    {
        var temporaryStoragePath = CreateTemporaryStoragePath();
        try
        {
            await using var factory = CreateFactoryWithTemporaryStorage(
                temporaryStoragePath,
                maxFileSizeBytes: 4);
            using var client = factory.CreateClient();
            using var content = new MultipartFormDataContent();
            content.Add(CreateCsvContent("12345"), "branchFile", "branch.csv");
            content.Add(CreateCsvContent("1234"), "bankFile", "bank.csv");

            using var response = await client.PostAsync("/api/reconciliations/compare/jobs", content);
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("FileTooLarge", body.RootElement.GetProperty("error").GetString());
            Assert.Contains("4 bytes", body.RootElement.GetProperty("message").GetString());
            Assert.False(HasTemporaryBatchDirectories(temporaryStoragePath));
        }
        finally
        {
            DeleteTemporaryStoragePath(temporaryStoragePath);
        }
    }

    [Fact]
    public async Task QueueFilesComparison_RejectsUnexpectedOrDuplicateMultipartSections()
    {
        var temporaryStoragePath = CreateTemporaryStoragePath();
        try
        {
            await using var factory = CreateFactoryWithTemporaryStorage(temporaryStoragePath);
            using var client = factory.CreateClient();
            using var unexpectedContent = new MultipartFormDataContent();
            unexpectedContent.Add(new StringContent("metadata"), "note");
            using var unexpectedResponse = await client.PostAsync(
                "/api/reconciliations/compare/jobs",
                unexpectedContent);
            using var unexpectedBody = await JsonDocument.ParseAsync(
                await unexpectedResponse.Content.ReadAsStreamAsync());

            using var duplicateContent = new MultipartFormDataContent();
            duplicateContent.Add(CreateCsvContent("first"), "branchFile", "branch.csv");
            duplicateContent.Add(CreateCsvContent("second"), "branchFile", "branch-2.csv");
            duplicateContent.Add(CreateCsvContent("bank"), "bankFile", "bank.csv");
            using var duplicateResponse = await client.PostAsync(
                "/api/reconciliations/compare/jobs",
                duplicateContent);
            using var duplicateBody = await JsonDocument.ParseAsync(
                await duplicateResponse.Content.ReadAsStreamAsync());

            Assert.Equal(HttpStatusCode.BadRequest, unexpectedResponse.StatusCode);
            Assert.Equal(
                "UnexpectedMultipartSection",
                unexpectedBody.RootElement.GetProperty("error").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
            Assert.Equal(
                "DuplicateFileField",
                duplicateBody.RootElement.GetProperty("error").GetString());
            Assert.False(HasTemporaryBatchDirectories(temporaryStoragePath));
        }
        finally
        {
            DeleteTemporaryStoragePath(temporaryStoragePath);
        }
    }

    [Fact]
    public async Task QueueFilesComparison_RequiresMultipartContentType()
    {
        using var response = await _client.PostAsync(
            "/api/reconciliations/compare/jobs",
            new StringContent("not-multipart"));
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidMultipartContent", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task QueueFilesComparison_RejectsInvalidExtensionBeforeCreatingBatch()
    {
        var temporaryStoragePath = CreateTemporaryStoragePath();
        try
        {
            await using var factory = CreateFactoryWithTemporaryStorage(temporaryStoragePath);
            using var client = factory.CreateClient();
            using var content = new MultipartFormDataContent();
            content.Add(CreateCsvContent("branch"), "branchFile", "branch.exe");
            content.Add(CreateCsvContent("bank"), "bankFile", "bank.csv");

            using var response = await client.PostAsync("/api/reconciliations/compare/jobs", content);
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            using var historyResponse = await client.GetAsync("/api/reconciliations");
            using var historyBody = await JsonDocument.ParseAsync(
                await historyResponse.Content.ReadAsStreamAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("InvalidFileExtension", body.RootElement.GetProperty("error").GetString());
            Assert.Empty(historyBody.RootElement.EnumerateArray());
            Assert.False(HasTemporaryBatchDirectories(temporaryStoragePath));
        }
        finally
        {
            DeleteTemporaryStoragePath(temporaryStoragePath);
        }
    }

    [Fact]
    public async Task CompareDatabaseSources_ReturnsBadRequest_WhenSourceIsInactive()
    {
        await using var factory = CreateFactoryWithDatabaseReader(
            new StubDatabaseSourceReader(new Dictionary<string, IReadOnlyList<TransactionRecord>>()));
        using var client = factory.CreateClient();
        var bankSourceId = new Guid("22222222-2222-2222-2222-222222222222");
        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/reconciliation-sources/{bankSourceId}",
            new { DisplayName = "Bank", Description = "", IsActive = false });

        using var response = await client.PostAsync(
            "/api/reconciliations/compare-database-sources",
            content: null);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InactiveReconciliationSource", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("BANK", body.RootElement.GetProperty("sourceName").GetString());
    }

    [Fact]
    public async Task CompareDatabaseSources_StoresFailedBatch_WhenReadFails()
    {
        var databaseReader = new StubDatabaseSourceReader(
            new Dictionary<string, IReadOnlyList<TransactionRecord>>(),
            failingSourceCode: "BANK");
        await using var factory = CreateFactoryWithDatabaseReader(databaseReader);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/reconciliations/compare-database-sources",
            content: null);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("DatabaseSourceReadFailed", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("BANK", body.RootElement.GetProperty("sourceName").GetString());

        using var historyResponse = await client.GetAsync("/api/reconciliations");
        using var historyBody = await JsonDocument.ParseAsync(await historyResponse.Content.ReadAsStreamAsync());
        Assert.Contains(historyBody.RootElement.EnumerateArray(), batch =>
            batch.GetProperty("status").GetString() == "Failed" &&
            batch.GetProperty("errorCode").GetString() == "DatabaseSourceReadFailed");
    }

    [Fact]
    public async Task Compare_UsesConfiguredCodeMappings_WhenMatchingRecords()
    {
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                beylikduzu sube,a fonu,TX001,2026-06-26,100,10000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """);

        using var response = await _client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, root.GetProperty("matchedCount").GetInt32());
        Assert.Equal(0, root.GetProperty("mismatchCount").GetInt32());

        var result = Assert.Single(root.GetProperty("results").EnumerateArray());
        Assert.Equal("Matched", result.GetProperty("status").GetString());
        Assert.Equal("BEYLIKDUZU", result.GetProperty("branchCode").GetString());
        Assert.Equal("A", result.GetProperty("fundCode").GetString());
    }

    [Fact]
    public async Task Compare_UsesConfiguredMatchingFields_WhenMatchingRecords()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<ReconciliationComparisonOptions>(options =>
                    {
                        options.MatchingFields = ["BranchCode", "TransactionNumber"];
                    });
                });
            });
        using var client = factory.CreateClient();
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,B,TX001,2026-06-26,100,10000
                """);

        using var response = await client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, root.GetProperty("matchedCount").GetInt32());
        Assert.Equal(0, root.GetProperty("onlyInBranchCount").GetInt32());
        Assert.Equal(0, root.GetProperty("onlyInBankCount").GetInt32());
    }

    [Fact]
    public async Task Compare_UsesConfiguredComparisonFields_WhenClassifyingRecords()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<ReconciliationComparisonOptions>(options =>
                    {
                        options.ComparisonFields = ["Amount"];
                    });
                });
            });
        using var client = factory.CreateClient();
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,90,10000
                """);

        using var response = await client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, root.GetProperty("matchedCount").GetInt32());
        Assert.Equal(0, root.GetProperty("mismatchCount").GetInt32());
    }

    [Fact]
    public async Task Compare_UsesConfiguredResultFields_WhenReturningResults()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<ReconciliationComparisonOptions>(options =>
                    {
                        options.ResultFields = ["BranchCode", "TransactionDate", "TransactionNumber"];
                    });
                });
            });
        using var client = factory.CreateClient();
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """);

        using var response = await client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var result = Assert.Single(body.RootElement.GetProperty("results").EnumerateArray());
        var fieldValues = result.GetProperty("fieldValues");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("BEYLIKDUZU", fieldValues.GetProperty("BranchCode").GetString());
        Assert.Equal("2026-06-26", fieldValues.GetProperty("TransactionDate").GetString());
        Assert.Equal("TX001", fieldValues.GetProperty("TransactionNumber").GetString());
        Assert.False(fieldValues.TryGetProperty("FundCode", out _));
    }

    [Fact]
    public async Task Compare_ReturnsBadRequest_WhenBranchFileIsMissing()
    {
        using var content = CreateMultipartContent(
            branchCsv: null,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """);

        using var response = await _client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("MissingBranchFile", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Compare_ReturnsBadRequest_WhenCsvIsInvalid()
    {
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,not-a-number,10000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """);

        using var response = await _client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidCsvFile", root.GetProperty("error").GetString());
        Assert.Equal(2, root.GetProperty("rowNumber").GetInt32());
        Assert.Equal("Quantity", root.GetProperty("columnName").GetString());
        Assert.Contains("Quantity must be a valid decimal number", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Compare_StoresFailedBatchInHistory_WhenCsvIsInvalid()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,not-a-number,10000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """);

        using var compareResponse = await client.PostAsync("/api/reconciliations/compare", content);
        using var historyResponse = await client.GetAsync("/api/reconciliations");
        using var historyBody = await JsonDocument.ParseAsync(await historyResponse.Content.ReadAsStreamAsync());
        var failedBatch = Assert.Single(historyBody.RootElement.EnumerateArray());

        Assert.Equal(HttpStatusCode.BadRequest, compareResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Equal("Failed", failedBatch.GetProperty("status").GetString());
        Assert.Equal("InvalidCsvFile", failedBatch.GetProperty("errorCode").GetString());
        Assert.Contains("Quantity must be a valid decimal number", failedBatch.GetProperty("errorMessage").GetString());
        Assert.Equal("branch-transactions.csv", failedBatch.GetProperty("branchFileName").GetString());
        Assert.Equal("bank-transactions.csv", failedBatch.GetProperty("bankFileName").GetString());
        Assert.True(failedBatch.GetProperty("processingDurationMilliseconds").GetInt64() >= 0);
        Assert.Equal(0, failedBatch.GetProperty("matchedCount").GetInt32());

        var batchId = failedBatch.GetProperty("id").GetGuid();
        using var detailResponse = await client.GetAsync($"/api/reconciliations/{batchId}");
        using var detailBody = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
        var detailRoot = detailBody.RootElement;

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal("Failed", detailRoot.GetProperty("status").GetString());
        Assert.Equal("InvalidCsvFile", detailRoot.GetProperty("errorCode").GetString());
        Assert.Equal(0, detailRoot.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Compare_AcceptsTxtFiles_WhenContentUsesExpectedDelimitedFormat()
    {
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode|FundCode|TransactionNumber|TransactionDate|Quantity|Amount
                BEYLIKDUZU|A|TX001|2026-06-26|100|10000
                """,
            bankCsv: """
                BranchCode|FundCode|TransactionNumber|TransactionDate|Quantity|Amount
                BEYLIKDUZU|A|TX001|2026-06-26|100|10000
                """,
            branchFileName: "branch-transactions.txt",
            bankFileName: "bank-transactions.txt");

        using var response = await _client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.RootElement.GetProperty("matchedCount").GetInt32());
        Assert.Equal("branch-transactions.txt", body.RootElement.GetProperty("branchFileName").GetString());
        Assert.Equal("bank-transactions.txt", body.RootElement.GetProperty("bankFileName").GetString());
    }

    [Fact]
    public async Task Compare_ReturnsBadRequest_WhenFileExtensionIsInvalid()
    {
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """,
            branchFileName: "branch-transactions.json");

        using var response = await _client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidFileExtension", body.RootElement.GetProperty("error").GetString());
        Assert.Contains(".csv, .txt", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Compare_ReturnsBadRequest_WhenFileIsTooLarge()
    {
        var oversizedCsv = new string('x', (5 * 1024 * 1024) + 1);
        using var content = CreateMultipartContent(
            branchCsv: oversizedCsv,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """);

        using var response = await _client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("FileTooLarge", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Compare_UsesConfiguredUploadLimit_WhenValidatingFileSize()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<ReconciliationUploadOptions>(options =>
                    {
                        options.MaxCsvFileSizeBytes = 20;
                    });
                });
            });
        using var client = factory.CreateClient();
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """);

        using var response = await client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("FileTooLarge", body.RootElement.GetProperty("error").GetString());
        Assert.Contains("20 bytes", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Compare_RequiresBackgroundJob_WhenFileExceedsSynchronousLimit()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<ReconciliationUploadOptions>(options =>
                    {
                        options.MaxCsvFileSizeBytes = 1024;
                        options.SynchronousComparisonMaxFileSizeBytes = 20;
                    });
                });
            });
        using var client = factory.CreateClient();
        using var content = CreateMultipartContent(
            branchCsv: "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount\nA,B,C,2026-01-01,1,1",
            bankCsv: "BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount\nA,B,C,2026-01-01,1,1");

        using var response = await client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("AsyncComparisonRequired", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Compare_ReturnsBadRequest_WhenDuplicateTransactionKeyExists()
    {
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """);

        using var response = await _client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("DuplicateTransactionKey", root.GetProperty("error").GetString());
        Assert.Equal("branch", root.GetProperty("sourceName").GetString());
        Assert.Equal("BEYLIKDUZU|A|TX001", root.GetProperty("matchingKey").GetString());
    }

    [Fact]
    public async Task Compare_StoresBatchInHistory_WhenFilesAreValid()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                BEYLIKDUZU,B,TX002,2026-06-26,50,5000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                BEYLIKDUZU,B,TX002,2026-06-26,45,5000
                """);

        using var compareResponse = await client.PostAsync("/api/reconciliations/compare", content);
        using var compareBody = await JsonDocument.ParseAsync(await compareResponse.Content.ReadAsStreamAsync());
        var batchId = compareBody.RootElement.GetProperty("batchId").GetGuid();

        using var historyResponse = await client.GetAsync("/api/reconciliations");
        using var historyBody = await JsonDocument.ParseAsync(await historyResponse.Content.ReadAsStreamAsync());
        var historyItem = historyBody.RootElement
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == batchId);

        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Equal(batchId, historyItem.GetProperty("id").GetGuid());
        Assert.Equal("Completed", historyItem.GetProperty("status").GetString());
        Assert.True(historyItem.GetProperty("processingDurationMilliseconds").GetInt64() >= 0);
        Assert.Equal(1, historyItem.GetProperty("matchedCount").GetInt32());
        Assert.Equal(1, historyItem.GetProperty("mismatchCount").GetInt32());

        using var detailResponse = await client.GetAsync($"/api/reconciliations/{batchId}");
        using var detailBody = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
        var detailRoot = detailBody.RootElement;

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(batchId, detailRoot.GetProperty("id").GetGuid());
        Assert.Equal("Completed", detailRoot.GetProperty("status").GetString());
        Assert.True(detailRoot.GetProperty("processingDurationMilliseconds").GetInt64() >= 0);
        Assert.Equal(2, detailRoot.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task GetReconciliationBatch_ReturnsNotFound_WhenBatchDoesNotExist()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/reconciliations/{Guid.NewGuid()}");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ReconciliationBatchNotFound", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ExportDifferences_ReturnsExcelReport_WithOnlyDifferenceRows()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                BEYLIKDUZU,B,TX002,2026-06-26,50,5000
                BEYLIKDUZU,C,TX003,2026-06-26,20,2000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                BEYLIKDUZU,B,TX002,2026-06-26,45,5000
                BEYLIKDUZU,D,TX004,2026-06-26,10,1000
                """);

        using var compareResponse = await client.PostAsync("/api/reconciliations/compare", content);
        using var compareBody = await JsonDocument.ParseAsync(await compareResponse.Content.ReadAsStreamAsync());
        var batchId = compareBody.RootElement.GetProperty("batchId").GetGuid();

        using var exportResponse = await client.GetAsync($"/api/reconciliations/{batchId}/export");
        var reportBytes = await exportResponse.Content.ReadAsByteArrayAsync();
        var worksheetXml = ReadWorksheetXml(reportBytes);

        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            exportResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("QuantityMismatch", worksheetXml);
        Assert.Contains("OnlyInBranch", worksheetXml);
        Assert.Contains("OnlyInBank", worksheetXml);
        Assert.DoesNotContain(">Matched<", worksheetXml);
        Assert.Contains("Adet sube tarafinda fazla gorunuyor.", worksheetXml);
        Assert.Contains("Sadece sube tarafinda var.", worksheetXml);
        Assert.Contains("Sadece banka tarafinda var.", worksheetXml);
    }

    [Fact]
    public async Task ExportDifferences_ReturnsNotFound_WhenBatchDoesNotExist()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/reconciliations/{Guid.NewGuid()}/export");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ReconciliationBatchNotFound", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetReconciliationSources_ReturnsDefaultBranchAndBankSources()
    {
        using var response = await _client.GetAsync("/api/reconciliation-sources");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var sources = body.RootElement.EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(sources, source =>
            source.GetProperty("type").GetString() == "Branch" &&
            source.GetProperty("code").GetString() == "BRANCH");
        Assert.Contains(sources, source =>
            source.GetProperty("type").GetString() == "Bank" &&
            source.GetProperty("code").GetString() == "BANK");
        Assert.All(sources, source =>
            Assert.False(source.GetProperty("isDatabaseConfigured").GetBoolean()));
    }

    [Fact]
    public async Task GetReconciliationSources_ReturnsConfiguredStatus_WithoutExposingConnectionString()
    {
        const string connectionValue = "Host=database.internal;Username=reader;Password=secret";
        await using var factory = new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:BranchSourceDatabase"] = connectionValue,
                        ["ReconciliationDatabaseSources:Sources:0:Code"] = "BRANCH",
                        ["ReconciliationDatabaseSources:Sources:0:ConnectionStringName"] = "BranchSourceDatabase",
                        ["ReconciliationDatabaseSources:Sources:0:Query"] = "SELECT * FROM branch_transactions"
                    });
                });
            });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/reconciliation-sources");
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        var branch = body.RootElement.EnumerateArray().Single(source =>
            source.GetProperty("code").GetString() == "BRANCH");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(branch.GetProperty("isDatabaseConfigured").GetBoolean());
        Assert.DoesNotContain(connectionValue, json);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateReconciliationSource_UpdatesEditableFields()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var sourceId = new Guid("11111111-1111-1111-1111-111111111111");

        using var response = await client.PutAsJsonAsync(
            $"/api/reconciliation-sources/{sourceId}",
            new
            {
                DisplayName = "Sube Verisi",
                Description = "Sube islemlerinin kaynagi.",
                IsActive = false
            });
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("BRANCH", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("Sube Verisi", body.RootElement.GetProperty("displayName").GetString());
        Assert.False(body.RootElement.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task UpdateReconciliationSource_ReturnsBadRequest_WhenDisplayNameIsBlank()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var sourceId = new Guid("11111111-1111-1111-1111-111111111111");

        using var response = await client.PutAsJsonAsync(
            $"/api/reconciliation-sources/{sourceId}",
            new { DisplayName = " ", Description = "", IsActive = true });
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidReconciliationSource", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task UpdateReconciliationSource_ReturnsNotFound_ForUnknownSource()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/reconciliation-sources/{Guid.NewGuid()}",
            new { DisplayName = "Unknown", Description = "", IsActive = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ManagementUpdate_ReturnsUnauthorized_ForAnonymousUser()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/reconciliation-sources/11111111-1111-1111-1111-111111111111")
        {
            Content = JsonContent.Create(new
            {
                DisplayName = "Sube",
                Description = "Aciklama",
                IsActive = true
            })
        };
        request.Headers.Add("X-Test-Anonymous", "true");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ManagementUpdates_CreateFilterableAuditEvents()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var sourceId = new Guid("11111111-1111-1111-1111-111111111111");

        using var sourceResponse = await client.PutAsJsonAsync(
            $"/api/reconciliation-sources/{sourceId}",
            new
            {
                DisplayName = "Sube Verisi",
                Description = "Audit testi.",
                IsActive = true
            });
        using var comparisonResponse = await client.PutAsJsonAsync(
            "/api/reconciliation-comparison-settings",
            new ReconciliationComparisonOptions
            {
                NormalizeCodeCase = true,
                TrimTextValues = true,
                MatchingFields = ["BranchCode", "FundCode", "TransactionNumber"],
                ComparisonFields = ["Quantity", "Amount"],
                ResultFields = ["BranchCode", "FundCode", "TransactionNumber"]
            });
        using var schemaResponse = await client.PutAsJsonAsync(
            "/api/reconciliation-file-schema",
            CreateSchemaUpdate());
        using var auditResponse = await client.GetAsync(
            "/api/reconciliation-audit-events?actor=test-administrator&take=50");
        var auditJson = await auditResponse.Content.ReadAsStringAsync();
        using var auditBody = JsonDocument.Parse(auditJson);
        var auditEvents = auditBody.RootElement.EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, sourceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, comparisonResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, schemaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        Assert.True(auditResponse.Headers.TryGetValues("X-Total-Count", out var counts));
        Assert.Equal("3", Assert.Single(counts));
        Assert.Contains(auditEvents, item =>
            item.GetProperty("action").GetString() == "SourceUpdated");
        Assert.Contains(auditEvents, item =>
            item.GetProperty("action").GetString() == "ComparisonSettingsUpdated");
        Assert.Contains(auditEvents, item =>
            item.GetProperty("action").GetString() == "FileSchemaUpdated");
        Assert.All(auditEvents, item =>
        {
            Assert.Equal("test-administrator", item.GetProperty("actor").GetString());
            Assert.Equal(JsonValueKind.Object, item.GetProperty("afterState").ValueKind);
        });
        Assert.DoesNotContain("Password", auditJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAuditEvents_ReturnsUnauthorized_ForAnonymousUser()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/reconciliation-audit-events");
        request.Headers.Add("X-Test-Anonymous", "true");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditRetentionStatus_ReturnsSanitizedOperationalSnapshot_ForAdministrator()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/reconciliation-audit-retention/status");
        request.Headers.Add("X-Test-Actor", "retention-administrator");
        request.Headers.Add("X-Test-Permission", "reconciliation.manage");

        using var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ready", body.RootElement.GetProperty("status").GetString());
        Assert.True(body.RootElement.GetProperty("enabled").GetBoolean());
        Assert.False(body.RootElement.GetProperty("immutableArchiveEnabled").GetBoolean());
        Assert.Equal(365, body.RootElement.GetProperty("hotRetentionDays").GetInt32());
        Assert.Equal(2555, body.RootElement.GetProperty("archiveRetentionDays").GetInt32());
        Assert.False(body.RootElement.GetProperty("alerting").GetBoolean());
        Assert.Empty(body.RootElement.GetProperty("alerts").EnumerateArray());
        Assert.False(json.Contains("signingKey", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("bucket", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("exception", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAuditRetentionStatus_ReturnsUnauthorized_ForAnonymousUser()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/reconciliation-audit-retention/status");
        request.Headers.Add("X-Test-Anonymous", "true");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetReconciliationFileSchema_ReturnsExpectedFixedColumns()
    {
        using var response = await _client.GetAsync("/api/reconciliation-file-schema");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var columns = body.RootElement.EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(6, columns.Length);

        Assert.Equal(1, columns[0].GetProperty("position").GetInt32());
        Assert.Equal("BranchCode", columns[0].GetProperty("field").GetString());
        Assert.Equal("BranchCode", columns[0].GetProperty("name").GetString());
        Assert.Equal("Text", columns[0].GetProperty("type").GetString());
        Assert.True(columns[0].GetProperty("required").GetBoolean());
        Assert.Contains("Matching key", columns[0].GetProperty("description").GetString());

        var transactionDate = columns.Single(column =>
            column.GetProperty("name").GetString() == "TransactionDate");
        Assert.Equal("Date", transactionDate.GetProperty("type").GetString());
        Assert.Equal("yyyy-MM-dd", transactionDate.GetProperty("dateFormat").GetString());
        Assert.Contains("yyyy-MM-dd", transactionDate.GetProperty("description").GetString());

        var transactionNumber = columns.Single(column =>
            column.GetProperty("name").GetString() == "TransactionNumber");
        Assert.Equal("^[A-Za-z0-9-]+$", transactionNumber.GetProperty("pattern").GetString());
        Assert.Contains("Harf, rakam", transactionNumber.GetProperty("patternDescription").GetString());

        var amount = columns.Single(column =>
            column.GetProperty("name").GetString() == "Amount");
        Assert.Equal("Decimal", amount.GetProperty("type").GetString());
        Assert.Contains("Decimal", amount.GetProperty("description").GetString());
    }

    [Fact]
    public async Task GetReconciliationComparisonSettings_ReturnsConfiguredFields()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/reconciliation-comparison-settings");
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.RootElement.GetProperty("normalizeCodeCase").GetBoolean());
        Assert.Contains(
            body.RootElement.GetProperty("matchingFields").EnumerateArray(),
            field => field.GetString() == "TransactionNumber");
    }

    [Fact]
    public async Task UpdateReconciliationComparisonSettings_AppliesToNextComparison()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var settings = new ReconciliationComparisonOptions
        {
            NormalizeCodeCase = true,
            TrimTextValues = true,
            MatchingFields = ["BranchCode", "TransactionNumber"],
            ComparisonFields = ["Quantity", "Amount"],
            ResultFields = ["BranchCode", "FundCode", "TransactionNumber"]
        };

        using var updateResponse = await client.PutAsJsonAsync(
            "/api/reconciliation-comparison-settings",
            settings);
        using var content = CreateMultipartContent(
            """
            BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
            BEYLIKDUZU,A,TX001,2026-06-26,100,10000
            """,
            """
            BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
            BEYLIKDUZU,B,TX001,2026-06-26,100,10000
            """);
        using var compareResponse = await client.PostAsync("/api/reconciliations/compare", content);
        using var compareBody = await JsonDocument.ParseAsync(await compareResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, compareResponse.StatusCode);
        Assert.Equal(1, compareBody.RootElement.GetProperty("matchedCount").GetInt32());
        Assert.Equal(
            "Matched",
            compareBody.RootElement.GetProperty("results")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task UpdateReconciliationComparisonSettings_ReturnsBadRequest_ForUnknownField()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var settings = new ReconciliationComparisonOptions
        {
            MatchingFields = ["BranchCode", "TransactionNumber"],
            ComparisonFields = ["UnknownField"],
            ResultFields = ["BranchCode"]
        };

        using var response = await client.PutAsJsonAsync(
            "/api/reconciliation-comparison-settings",
            settings);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidComparisonSettings", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task UpdateReconciliationFileSchema_UpdatesRuntimeSchema()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        using var updateResponse = await client.PutAsJsonAsync(
            "/api/reconciliation-file-schema",
            CreateSchemaUpdate(
                branchName: "SubeKodu",
                fundName: "FonKodu",
                transactionNumberName: "IslemNo",
                transactionDateName: "Tarih",
                quantityName: "Adet",
                amountName: "Tutar"));

        using var updateBody = await JsonDocument.ParseAsync(await updateResponse.Content.ReadAsStreamAsync());
        var updatedColumns = updateBody.RootElement.EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Contains(updatedColumns, column =>
            column.GetProperty("field").GetString() == "BranchCode" &&
            column.GetProperty("name").GetString() == "SubeKodu");

        using var content = CreateValidationMultipartContent("""
            SubeKodu,FonKodu,IslemNo,Tarih,Adet,Tutar
            BEYLIKDUZU,A,TX001,2026-06-26,100,10000
            """);
        using var validationResponse = await client.PostAsync("/api/reconciliation-file-schema/validate", content);
        using var validationBody = await JsonDocument.ParseAsync(await validationResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        Assert.True(validationBody.RootElement.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task UpdateReconciliationFileSchema_AppliesFixedWidthTxtToComparison()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var schema = CreateFixedWidthSchemaUpdate();
        var header = CreateFixedWidthLine(schema, schema.Columns.Select(column => column.Name).ToArray());
        var branch = CreateFixedWidthLine(
            schema,
            ["BEYLIKDUZU", "A", "TX001", "2026-06-26", "100", "10000"]);
        var bank = CreateFixedWidthLine(
            schema,
            ["BEYLIKDUZU", "A", "TX001", "2026-06-26", "100", "9990"]);

        using var updateResponse = await client.PutAsJsonAsync("/api/reconciliation-file-schema", schema);
        using var content = CreateMultipartContent(
            $"{header}\n{branch}",
            $"{header}\n{bank}",
            "branch-fixed.txt",
            "bank-fixed.txt");
        using var compareResponse = await client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await compareResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, compareResponse.StatusCode);
        Assert.Equal(1, body.RootElement.GetProperty("mismatchCount").GetInt32());
        Assert.Equal(
            "AmountMismatch",
            Assert.Single(body.RootElement.GetProperty("results").EnumerateArray())
                .GetProperty("status")
                .GetString());
    }

    [Fact]
    public async Task UpdateReconciliationFileSchema_ReturnsBadRequest_WhenColumnNamesAreDuplicate()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.PutAsJsonAsync(
            "/api/reconciliation-file-schema",
            CreateSchemaUpdate(branchName: "Code", fundName: " code "));
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidFileSchema", body.RootElement.GetProperty("error").GetString());
        Assert.Contains("unique", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UpdateReconciliationFileSchema_AppliesConfiguredLengthRules()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var schema = CreateSchemaUpdate();
        var transactionNumberColumn = Assert.Single(
            schema.Columns,
            column => column.Field == "TransactionNumber");
        transactionNumberColumn.MaxLength = 5;

        using var updateResponse = await client.PutAsJsonAsync("/api/reconciliation-file-schema", schema);
        using var content = CreateValidationMultipartContent("""
            BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
            BEYLIKDUZU,A,TX0019,2026-06-26,100,10000
            """);
        using var validationResponse = await client.PostAsync("/api/reconciliation-file-schema/validate", content);
        using var validationBody = await JsonDocument.ParseAsync(await validationResponse.Content.ReadAsStreamAsync());
        var root = validationBody.RootElement;

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        Assert.False(root.GetProperty("isValid").GetBoolean());
        Assert.Equal("TransactionNumber", root.GetProperty("columnName").GetString());
        Assert.Contains("5 characters or fewer", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UpdateReconciliationFileSchema_AppliesConfiguredAllowedValues()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var schema = CreateSchemaUpdate();
        var fundCodeColumn = Assert.Single(
            schema.Columns,
            column => column.Field == "FundCode");
        fundCodeColumn.AllowedValues = ["A", "B"];

        using var updateResponse = await client.PutAsJsonAsync("/api/reconciliation-file-schema", schema);
        using var content = CreateValidationMultipartContent("""
            BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
            BEYLIKDUZU,C,TX001,2026-06-26,100,10000
            """);
        using var validationResponse = await client.PostAsync("/api/reconciliation-file-schema/validate", content);
        using var validationBody = await JsonDocument.ParseAsync(await validationResponse.Content.ReadAsStreamAsync());
        var root = validationBody.RootElement;

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        Assert.False(root.GetProperty("isValid").GetBoolean());
        Assert.Equal("FundCode", root.GetProperty("columnName").GetString());
        Assert.Contains("A, B", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UpdateReconciliationFileSchema_AppliesConfiguredNumericRanges()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var schema = CreateSchemaUpdate();
        var amountColumn = Assert.Single(
            schema.Columns,
            column => column.Field == "Amount");
        amountColumn.MinValue = 1;
        amountColumn.MaxValue = 1000;

        using var updateResponse = await client.PutAsJsonAsync("/api/reconciliation-file-schema", schema);
        using var content = CreateValidationMultipartContent("""
            BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
            BEYLIKDUZU,A,TX001,2026-06-26,100,0
            """);
        using var validationResponse = await client.PostAsync("/api/reconciliation-file-schema/validate", content);
        using var validationBody = await JsonDocument.ParseAsync(await validationResponse.Content.ReadAsStreamAsync());
        var root = validationBody.RootElement;

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        Assert.False(root.GetProperty("isValid").GetBoolean());
        Assert.Equal("Amount", root.GetProperty("columnName").GetString());
        Assert.Contains("greater than or equal to 1", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UpdateReconciliationFileSchema_AppliesConfiguredMaxDecimalPlaces()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory();
        using var client = factory.CreateClient();
        var schema = CreateSchemaUpdate();
        var amountColumn = Assert.Single(
            schema.Columns,
            column => column.Field == "Amount");
        amountColumn.MaxDecimalPlaces = 2;

        using var updateResponse = await client.PutAsJsonAsync("/api/reconciliation-file-schema", schema);
        using var content = CreateValidationMultipartContent("""
            BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
            BEYLIKDUZU,A,TX001,2026-06-26,100,10000.123
            """);
        using var validationResponse = await client.PostAsync("/api/reconciliation-file-schema/validate", content);
        using var validationBody = await JsonDocument.ParseAsync(await validationResponse.Content.ReadAsStreamAsync());
        var root = validationBody.RootElement;

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        Assert.False(root.GetProperty("isValid").GetBoolean());
        Assert.Equal("Amount", root.GetProperty("columnName").GetString());
        Assert.Contains("2 decimal places or fewer", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UpdateReconciliationFileSchema_AllowsExtraColumnsAndReturnsExtraFields()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<ReconciliationComparisonOptions>(options =>
                    {
                        options.ComparisonFields = ["Quantity", "Amount", "Commission"];
                        options.ResultFields = ["BranchCode", "Commission"];
                    });
                });
            });
        using var client = factory.CreateClient();
        var schema = CreateSchemaUpdate();
        schema.Columns =
        [
            .. schema.Columns,
            new()
            {
                Field = "Commission",
                Name = "Commission",
                Type = "Decimal",
                Required = false,
                MaxDecimalPlaces = 2,
                Description = "Komisyon tutari."
            }
        ];

        using var updateResponse = await client.PutAsJsonAsync("/api/reconciliation-file-schema", schema);
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount,Commission
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000,12.34
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount,Commission
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000,10.00
                """);
        using var compareResponse = await client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await compareResponse.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        var result = Assert.Single(root.GetProperty("results").EnumerateArray());
        var branchRecord = result.GetProperty("branchRecord");
        var fieldValues = result.GetProperty("fieldValues");
        var fieldDifferences = result.GetProperty("fieldDifferences");
        var batchId = root.GetProperty("batchId").GetGuid();
        using var exportResponse = await client.GetAsync($"/api/reconciliations/{batchId}/export");
        var worksheetXml = ReadWorksheetXml(await exportResponse.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, compareResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.Equal("FieldMismatch", result.GetProperty("status").GetString());
        Assert.Equal("12.34", branchRecord.GetProperty("extraFields").GetProperty("Commission").GetString());
        Assert.Equal("12.34", fieldValues.GetProperty("Commission").GetString());
        Assert.Equal(2.34m, fieldDifferences.GetProperty("Commission").GetDecimal());
        Assert.Contains("CommissionDifference", worksheetXml);
        Assert.Contains("FieldMismatch", worksheetXml);
        Assert.Contains("Ek alan farki var", worksheetXml);
    }

    [Fact]
    public async Task ValidateReconciliationFileSchema_ReturnsValidResult_WhenFileMatchesSchema()
    {
        using var content = CreateValidationMultipartContent("""
            BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
            BEYLIKDUZU,A,TX001,2026-06-26,100,10000
            KADIKOY,B,TX002,2026-06-27,50,5000
            """);

        using var response = await _client.PostAsync("/api/reconciliation-file-schema/validate", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(root.GetProperty("isValid").GetBoolean());
        Assert.Equal(2, root.GetProperty("recordCount").GetInt32());
    }

    [Fact]
    public async Task ValidateReconciliationFileSchema_ReturnsInvalidResult_WhenFileDoesNotMatchSchema()
    {
        using var content = CreateValidationMultipartContent("""
            BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
            BEYLIKDUZU,A,TX001,2026-06-26,100,not-a-number
            """);

        using var response = await _client.PostAsync("/api/reconciliation-file-schema/validate", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(root.GetProperty("isValid").GetBoolean());
        Assert.Equal("InvalidCsvFile", root.GetProperty("error").GetString());
        Assert.Equal(2, root.GetProperty("rowNumber").GetInt32());
        Assert.Equal("Amount", root.GetProperty("columnName").GetString());
    }

    [Fact]
    public async Task ValidateReconciliationFileSchema_ReturnsAllValidationErrors()
    {
        using var content = CreateValidationMultipartContent("""
            BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
            BEYLIKDUZU,A,TX001,invalid-date,invalid-quantity,invalid-amount
            KADIKOY,B,TX002,2026-06-27,invalid-quantity,5000
            """);

        using var response = await _client.PostAsync("/api/reconciliation-file-schema/validate", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        var errors = root.GetProperty("errors").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(root.GetProperty("isValid").GetBoolean());
        Assert.Equal(2, root.GetProperty("recordCount").GetInt32());
        Assert.Equal(4, errors.Length);
        Assert.Contains(errors, error =>
            error.GetProperty("rowNumber").GetInt32() == 2 &&
            error.GetProperty("columnName").GetString() == "TransactionDate");
        Assert.Contains(errors, error =>
            error.GetProperty("rowNumber").GetInt32() == 3 &&
            error.GetProperty("columnName").GetString() == "Quantity");
    }

    [Fact]
    public async Task ValidateReconciliationFileSchema_UsesConfiguredIntegerRule()
    {
        await using var factory = new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<ReconciliationFileSchemaOptions>(options =>
                    {
                        options.Columns = CreateSchemaWithIntegerTransactionNumber().Columns;
                    });
                });
            });
        using var client = factory.CreateClient();
        using var content = CreateValidationMultipartContent("""
            BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
            BEYLIKDUZU,A,TX001,2026-06-26,100,10000
            """);

        using var response = await client.PostAsync("/api/reconciliation-file-schema/validate", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(root.GetProperty("isValid").GetBoolean());
        Assert.Equal(2, root.GetProperty("rowNumber").GetInt32());
        Assert.Equal("TransactionNumber", root.GetProperty("columnName").GetString());
        Assert.Contains("integer", root.GetProperty("message").GetString());
    }

    private async Task<Guid> CreateCompletedBatchAsync()
    {
        using var content = CreateMultipartContent(
            branchCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """,
            bankCsv: """
                BranchCode,FundCode,TransactionNumber,TransactionDate,Quantity,Amount
                BEYLIKDUZU,A,TX001,2026-06-26,100,10000
                """);
        using var response = await _client.PostAsync("/api/reconciliations/compare", content);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Pending", body.RootElement.GetProperty("approvalStatus").GetString());
        return body.RootElement.GetProperty("batchId").GetGuid();
    }

    private static HttpRequestMessage CreateApprovalRequest(
        Guid batchId,
        string decision,
        string? comment = null)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/reconciliations/{batchId}/approval")
        {
            Content = JsonContent.Create(new { decision, comment })
        };
    }

    private static MultipartFormDataContent CreateMultipartContent(
        string? branchCsv,
        string? bankCsv,
        string branchFileName = "branch-transactions.csv",
        string bankFileName = "bank-transactions.csv")
    {
        var content = new MultipartFormDataContent();

        if (branchCsv is not null)
        {
            content.Add(CreateCsvContent(branchCsv), "branchFile", branchFileName);
        }

        if (bankCsv is not null)
        {
            content.Add(CreateCsvContent(bankCsv), "bankFile", bankFileName);
        }

        return content;
    }

    private static async Task<JsonDocument> WaitForBatchStatusAsync(
        HttpClient client,
        Guid batchId,
        string expectedStatus)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var response = await client.GetAsync($"/api/reconciliations/{batchId}");
            var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (document.RootElement.GetProperty("status").GetString() == expectedStatus)
            {
                return document;
            }

            document.Dispose();
            await Task.Delay(20);
        }

        throw new TimeoutException($"Batch '{batchId}' did not reach status '{expectedStatus}'.");
    }

    private static WebApplicationFactory<Program> CreateFactoryWithDatabaseReader(
        IReconciliationDatabaseSourceReader databaseSourceReader)
    {
        return new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IReconciliationDatabaseSourceReader>();
                    services.AddSingleton(databaseSourceReader);
                });
            });
    }

    private static WebApplicationFactory<Program> CreateFactoryWithTemporaryStorage(
        string storagePath,
        long? maxFileSizeBytes = null)
    {
        return new BankingReconciliationWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<ReconciliationUploadOptions>(options =>
                    {
                        options.TemporaryStoragePath = storagePath;
                        if (maxFileSizeBytes is not null)
                        {
                            options.MaxCsvFileSizeBytes = maxFileSizeBytes.Value;
                        }
                    });
                });
            });
    }

    private static bool HasTemporaryBatchDirectories(string storagePath) =>
        Directory.Exists(storagePath) && Directory.EnumerateDirectories(storagePath).Any();

    private static async Task WaitForTemporaryFilesToBeDeletedAsync(
        IReconciliationTemporaryFileStore temporaryFileStore,
        Guid batchId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (!await temporaryFileStore.ExistsAsync(batchId))
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    private static string CreateTemporaryStoragePath() => Path.Combine(
        Path.GetTempPath(),
        "BankingReconciliation.Tests",
        Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryStoragePath(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static TransactionRecord CreateDatabaseRecord(
        string transactionNumber,
        decimal amount,
        decimal quantity = 100)
    {
        return new TransactionRecord
        {
            BranchCode = "BEYLIKDUZU",
            FundCode = "A",
            TransactionNumber = transactionNumber,
            TransactionDate = new DateOnly(2026, 6, 26),
            Quantity = quantity,
            Amount = amount
        };
    }

    private static MultipartFormDataContent CreateValidationMultipartContent(
        string csv,
        string fileName = "transactions.csv")
    {
        var content = new MultipartFormDataContent();
        content.Add(CreateCsvContent(csv), "file", fileName);

        return content;
    }

    private static string ReadWorksheetXml(byte[] reportBytes)
    {
        using var stream = new MemoryStream(reportBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var worksheet = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(worksheet);

        using var worksheetStream = worksheet.Open();
        using var reader = new StreamReader(worksheetStream);

        return reader.ReadToEnd();
    }

    private static ByteArrayContent CreateCsvContent(string csv)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        content.Headers.ContentType = new("text/csv");

        return content;
    }

    private static ReconciliationFileSchemaOptions CreateSchemaWithIntegerTransactionNumber()
    {
        return new ReconciliationFileSchemaOptions
        {
            Columns =
            [
                new()
                {
                    Field = "BranchCode",
                    Name = "BranchCode",
                    Type = "Text",
                    Required = true
                },
                new()
                {
                    Field = "FundCode",
                    Name = "FundCode",
                    Type = "Text",
                    Required = true
                },
                new()
                {
                    Field = "TransactionNumber",
                    Name = "TransactionNumber",
                    Type = "Integer",
                    Required = true
                },
                new()
                {
                    Field = "TransactionDate",
                    Name = "TransactionDate",
                    Type = "Date",
                    Required = true,
                    DateFormat = "yyyy-MM-dd"
                },
                new()
                {
                    Field = "Quantity",
                    Name = "Quantity",
                    Type = "Decimal",
                    Required = true
                },
                new()
                {
                    Field = "Amount",
                    Name = "Amount",
                    Type = "Decimal",
                    Required = true
                }
            ]
        };
    }

    private static ReconciliationFileSchemaOptions CreateFixedWidthSchemaUpdate()
    {
        var schema = CreateSchemaUpdate();
        var lengths = new[] { 14, 10, 20, 15, 14, 16 };
        var start = 1;

        for (var index = 0; index < schema.Columns.Length; index++)
        {
            schema.Columns[index].FixedWidthStart = start;
            schema.Columns[index].FixedWidthLength = lengths[index];
            start += lengths[index];
        }

        return schema;
    }

    private static string CreateFixedWidthLine(
        ReconciliationFileSchemaOptions schema,
        IReadOnlyList<string> values)
    {
        return string.Concat(schema.Columns.Select((column, index) =>
            values[index].PadRight(column.FixedWidthLength!.Value)));
    }

    private static ReconciliationFileSchemaOptions CreateSchemaUpdate(
        string branchName = "BranchCode",
        string fundName = "FundCode",
        string transactionNumberName = "TransactionNumber",
        string transactionDateName = "TransactionDate",
        string quantityName = "Quantity",
        string amountName = "Amount")
    {
        return new ReconciliationFileSchemaOptions
        {
            Columns =
            [
                new()
                {
                    Field = "BranchCode",
                    Name = branchName,
                    Type = "Text",
                    Required = true,
                    Description = "Sube/kaynak kodu. Matching key parcasidir."
                },
                new()
                {
                    Field = "FundCode",
                    Name = fundName,
                    Type = "Text",
                    Required = true,
                    Description = "Fon kodu. Matching key parcasidir."
                },
                new()
                {
                    Field = "TransactionNumber",
                    Name = transactionNumberName,
                    Type = "Text",
                    Required = true,
                    Pattern = "^[A-Za-z0-9-]+$",
                    PatternDescription = "Harf, rakam ve tire icerebilir.",
                    Description = "Islem numarasi. Matching key parcasidir."
                },
                new()
                {
                    Field = "TransactionDate",
                    Name = transactionDateName,
                    Type = "Date",
                    Required = true,
                    DateFormat = "yyyy-MM-dd",
                    Description = "Islem tarihi. yyyy-MM-dd formatinda olmalidir."
                },
                new()
                {
                    Field = "Quantity",
                    Name = quantityName,
                    Type = "Decimal",
                    Required = true,
                    Description = "Adet. Decimal sayi olmalidir."
                },
                new()
                {
                    Field = "Amount",
                    Name = amountName,
                    Type = "Decimal",
                    Required = true,
                    Description = "Tutar. Decimal sayi olmalidir."
                }
            ]
        };
    }

    private sealed class StubDatabaseSourceReader : IReconciliationDatabaseSourceReader
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<TransactionRecord>> _records;
        private readonly string? _failingSourceCode;

        public StubDatabaseSourceReader(
            IReadOnlyDictionary<string, IReadOnlyList<TransactionRecord>> records,
            string? failingSourceCode = null)
        {
            _records = records;
            _failingSourceCode = failingSourceCode;
        }

        public Task<IReadOnlyList<TransactionRecord>> ReadAsync(
            string sourceCode,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(sourceCode, _failingSourceCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ReconciliationDatabaseSourceException(
                    sourceCode,
                    $"Database source '{sourceCode}' could not be read.");
            }

            return Task.FromResult(
                _records.TryGetValue(sourceCode, out var records)
                    ? records
                    : (IReadOnlyList<TransactionRecord>)[]);
        }
    }
}
