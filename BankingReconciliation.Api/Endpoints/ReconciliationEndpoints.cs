using System.Diagnostics;
using System.Security.Claims;
using BankingReconciliation.Api.Contracts;
using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Security;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Endpoints;

public static class ReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/reconciliations/compare", CompareAsync)
            .WithName("CompareReconciliationFiles")
            .Produces<ReconciliationSummaryResponse>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status409Conflict)
            .DisableAntiforgery();

        app.MapGet("/api/reconciliation-runtime-settings", GetRuntimeSettings)
            .WithName("GetReconciliationRuntimeSettings")
            .Produces(StatusCodes.Status200OK);

        app.MapPost("/api/reconciliations/compare/jobs", QueueFilesComparisonAsync)
            .WithName("QueueReconciliationFilesComparison")
            .Produces<ReconciliationJobAcceptedResponse>(StatusCodes.Status202Accepted)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .DisableAntiforgery();

        app.MapPost("/api/reconciliations/compare-database-sources", CompareDatabaseSourcesAsync)
            .WithName("CompareReconciliationDatabaseSources")
            .Produces<ReconciliationSummaryResponse>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest);

        app.MapPost("/api/reconciliations/compare-database-sources/jobs", QueueDatabaseSourcesComparison)
            .WithName("QueueReconciliationDatabaseSourcesComparison")
            .Produces<ReconciliationJobAcceptedResponse>(StatusCodes.Status202Accepted)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/api/reconciliations", GetHistory)
            .WithName("GetReconciliationHistory")
            .Produces<List<ReconciliationBatchListItemResponse>>(StatusCodes.Status200OK);

        app.MapGet("/api/reconciliations/{id:guid}", GetById)
            .WithName("GetReconciliationBatch")
            .Produces<ReconciliationBatchResponse>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status404NotFound);

        app.MapPost("/api/reconciliations/{id:guid}/approval", DecideApproval)
            .WithName("DecideReconciliationApproval")
            .RequireAuthorization(ReconciliationAuthorizationPolicies.Approver)
            .Produces<ReconciliationBatchResponse>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status409Conflict);

        app.MapGet("/api/reconciliations/{id:guid}/export", ExportDifferences)
            .WithName("ExportReconciliationDifferences")
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status404NotFound);

        app.MapGet("/api/reconciliation-file-schema", GetFileSchema)
            .WithName("GetReconciliationFileSchema")
            .Produces<List<ReconciliationFileSchemaColumnResponse>>(StatusCodes.Status200OK);

        app.MapPut("/api/reconciliation-file-schema", UpdateFileSchema)
            .WithName("UpdateReconciliationFileSchema")
            .RequireAuthorization(ReconciliationAuthorizationPolicies.Administrator)
            .Produces<List<ReconciliationFileSchemaColumnResponse>>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapGet("/api/reconciliation-comparison-settings", GetComparisonSettings)
            .WithName("GetReconciliationComparisonSettings")
            .Produces<ReconciliationComparisonOptions>(StatusCodes.Status200OK);

        app.MapPut("/api/reconciliation-comparison-settings", UpdateComparisonSettings)
            .WithName("UpdateReconciliationComparisonSettings")
            .RequireAuthorization(ReconciliationAuthorizationPolicies.Administrator)
            .Produces<ReconciliationComparisonOptions>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/api/reconciliation-file-schema/validate", ValidateFileSchemaAsync)
            .WithName("ValidateReconciliationFileSchema")
            .Produces<ReconciliationFileValidationResponse>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .DisableAntiforgery();

        return app;
    }

    private static IResult GetRuntimeSettings(IOptions<ReconciliationUploadOptions> uploadOptions)
    {
        return Results.Ok(new
        {
            synchronousComparisonMaxFileSizeBytes = Math.Min(
                uploadOptions.Value.SynchronousComparisonMaxFileSizeBytes,
                uploadOptions.Value.MaxCsvFileSizeBytes),
            maxCsvFileSizeBytes = uploadOptions.Value.MaxCsvFileSizeBytes
        });
    }

    private static IResult GetFileSchema(ReconciliationFileSchemaStore fileSchemaStore)
    {
        return Results.Ok(CsvTransactionFileParser.GetSchema(fileSchemaStore.GetOptions()));
    }

    private static IResult UpdateFileSchema(
        ReconciliationFileSchemaOptions fileSchemaOptions,
        ClaimsPrincipal user,
        IServiceProvider serviceProvider,
        ReconciliationFileSchemaStore fileSchemaStore,
        IReconciliationFileSchemaRepository fileSchemaRepository,
        IReconciliationAuditRepository auditRepository)
    {
        if (!ReconciliationFileSchemaOptionsValidator.HasRequiredTransactionFields(fileSchemaOptions))
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidFileSchema",
                Message = "File schema must contain each required transaction field exactly once."
            });
        }

        if (!ReconciliationFileSchemaOptionsValidator.HasValidColumnDefinitions(fileSchemaOptions))
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidFileSchema",
                Message = "File schema columns must have valid names, types, date formats, patterns, length rules, numeric ranges, decimal places, allowed values, and non-overlapping fixed-width positions."
            });
        }

        if (!ReconciliationFileSchemaOptionsValidator.HasUniqueColumnNames(fileSchemaOptions))
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidFileSchema",
                Message = "File schema column names must be unique."
            });
        }

        if (!ReconciliationFileSchemaOptionsValidator.HasUniqueFieldNames(fileSchemaOptions))
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidFileSchema",
                Message = "File schema field names must be unique."
            });
        }

        var actor = ReconciliationUserIdentity.GetActor(user);
        if (actor is null)
        {
            return Results.Forbid();
        }

        using var transaction = serviceProvider
            .GetService<ReconciliationDbContext>()?
            .Database.BeginTransaction();
        var beforeState = fileSchemaStore.GetOptions();
        fileSchemaRepository.Save(fileSchemaOptions);
        var afterState = ReconciliationFileSchemaStore.Clone(fileSchemaOptions);
        auditRepository.Add(
            ReconciliationAuditAction.FileSchemaUpdated,
            actor,
            ReconciliationAuditResourceType.FileSchema,
            "active",
            beforeState,
            afterState);
        transaction?.Commit();
        fileSchemaStore.Update(afterState);

        return Results.Ok(CsvTransactionFileParser.GetSchema(afterState));
    }

    private static async Task<IResult> ValidateFileSchemaAsync(
        IFormFile? file,
        ITransactionFileParser parser,
        IOptions<ReconciliationUploadOptions> uploadOptions,
        CancellationToken cancellationToken)
    {
        var fileError = ValidateUploadedFile(
            file,
            fieldName: "file",
            missingError: "MissingFile",
            allowedFileExtensions: uploadOptions.Value.AllowedFileExtensions,
            maxFileSizeBytes: uploadOptions.Value.MaxCsvFileSizeBytes);
        if (fileError is not null)
        {
            return Results.BadRequest(fileError);
        }

        var validation = await parser.ValidateAsync(file!, cancellationToken);
        if (validation.IsValid)
        {
            return Results.Ok(new ReconciliationFileValidationResponse
            {
                IsValid = true,
                RecordCount = validation.RecordCount
            });
        }

        var firstError = validation.Errors[0];
        return Results.Ok(new ReconciliationFileValidationResponse
        {
            IsValid = false,
            RecordCount = validation.RecordCount,
            Error = "InvalidCsvFile",
            Message = firstError.Message,
            RowNumber = firstError.RowNumber,
            ColumnName = firstError.ColumnName,
            Errors = validation.Errors
                .Select(error => new ReconciliationFileValidationErrorResponse
                {
                    Message = error.Message,
                    RowNumber = error.RowNumber,
                    ColumnName = error.ColumnName,
                    Rule = GetValidationRule(error)
                })
                .ToList()
        });
    }

    private static string GetValidationRule(CsvTransactionFileParseException error)
    {
        var message = error.Message;
        if (message.Contains("required", StringComparison.OrdinalIgnoreCase)) return "Zorunlu alan";
        if (message.Contains("format", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("date", StringComparison.OrdinalIgnoreCase)) return "Tarih biçimi";
        if (message.Contains("decimal", StringComparison.OrdinalIgnoreCase)) return "Ondalık basamak";
        if (message.Contains("minimum", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("maximum", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("range", StringComparison.OrdinalIgnoreCase)) return "Değer aralığı";
        if (message.Contains("length", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("characters", StringComparison.OrdinalIgnoreCase)) return "Uzunluk";
        if (message.Contains("allowed", StringComparison.OrdinalIgnoreCase)) return "İzin verilen değer";
        if (message.Contains("pattern", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("match", StringComparison.OrdinalIgnoreCase)) return "Metin deseni";
        if (string.Equals(error.ColumnName, "Header", StringComparison.OrdinalIgnoreCase)) return "Dosya başlığı";
        if (string.Equals(error.ColumnName, "Row", StringComparison.OrdinalIgnoreCase)) return "Satır yapısı";
        return "Veri tipi ve kolon kuralı";
    }

    private static IResult GetComparisonSettings(
        ReconciliationComparisonOptionsStore comparisonOptionsStore)
    {
        return Results.Ok(comparisonOptionsStore.GetOptions());
    }

    private static IResult UpdateComparisonSettings(
        ReconciliationComparisonOptions options,
        ClaimsPrincipal user,
        IServiceProvider serviceProvider,
        ReconciliationComparisonOptionsStore comparisonOptionsStore,
        IReconciliationComparisonOptionsRepository comparisonOptionsRepository,
        ReconciliationFileSchemaStore fileSchemaStore,
        IReconciliationAuditRepository auditRepository)
    {
        if (!ReconciliationComparisonOptionsValidator.HasValidDecimalPlaces(options) ||
            !ReconciliationComparisonOptionsValidator.HasValidMatchingFields(options) ||
            !ReconciliationComparisonOptionsValidator.HasValidComparisonFields(options) ||
            !ReconciliationComparisonOptionsValidator.HasValidResultFields(options) ||
            !ReconciliationComparisonOptionsValidator.HasValidMappings(options) ||
            !ReconciliationComparisonOptionsValidator.HasFieldsCompatibleWithSchema(
                options,
                fileSchemaStore.GetOptions()))
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidComparisonSettings",
                Message = "Comparison settings contain invalid fields, decimal places, value mappings, or fields that are incompatible with the active file schema."
            });
        }

        var actor = ReconciliationUserIdentity.GetActor(user);
        if (actor is null)
        {
            return Results.Forbid();
        }

        using var transaction = serviceProvider
            .GetService<ReconciliationDbContext>()?
            .Database.BeginTransaction();
        var beforeState = comparisonOptionsStore.GetOptions();
        comparisonOptionsRepository.Save(options);
        var afterState = ReconciliationComparisonOptionsStore.Clone(options);
        auditRepository.Add(
            ReconciliationAuditAction.ComparisonSettingsUpdated,
            actor,
            ReconciliationAuditResourceType.ComparisonSettings,
            "active",
            beforeState,
            afterState);
        transaction?.Commit();
        comparisonOptionsStore.Update(afterState);

        return Results.Ok(afterState);
    }

    private static IResult GetHistory(
        IReconciliationHistoryRepository historyRepository,
        HttpResponse response,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ReconciliationBatchStatus? status,
        ReconciliationInputType? inputType,
        int skip = 0,
        int take = 50)
    {
        if (skip < 0 ||
            take is < 1 or > 200 ||
            search?.Length > 200 ||
            (from is not null && to is not null && from > to))
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidHistoryQuery",
                Message = "History query requires skip >= 0, take between 1 and 200, search up to 200 characters, and a valid date range."
            });
        }

        var query = new ReconciliationHistoryQuery
        {
            Search = search,
            From = from,
            To = to,
            Status = status,
            InputType = inputType,
            Skip = skip,
            Take = take
        };
        var history = historyRepository
            .GetAll(query)
            .Select(batch => batch.ToListItemResponse())
            .ToList();
        response.Headers.Append("X-Total-Count", historyRepository.Count(query).ToString());

        return Results.Ok(history);
    }

    private static IResult GetById(
        Guid id,
        IReconciliationHistoryRepository historyRepository)
    {
        var batch = historyRepository.GetById(id);
        if (batch is null)
        {
            return Results.NotFound(new ReconciliationErrorResponse
            {
                Error = "ReconciliationBatchNotFound",
                Message = $"Reconciliation batch '{id}' was not found."
            });
        }

        return Results.Ok(batch.ToDetailResponse());
    }

    private static IResult DecideApproval(
        Guid id,
        ReconciliationApprovalDecisionRequest request,
        ClaimsPrincipal user,
        IServiceProvider serviceProvider,
        IReconciliationHistoryRepository historyRepository,
        IReconciliationAuditRepository auditRepository)
    {
        if (!Enum.IsDefined(request.Decision))
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidApprovalDecision",
                Message = "Decision must be Approve or Reject."
            });
        }

        var comment = string.IsNullOrWhiteSpace(request.Comment)
            ? null
            : request.Comment.Trim();
        if (comment?.Length > 1000)
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "ApprovalCommentTooLong",
                Message = "Approval comment cannot exceed 1000 characters."
            });
        }

        if (request.Decision == ReconciliationApprovalDecision.Reject && comment is null)
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "RejectionCommentRequired",
                Message = "A comment is required when rejecting a reconciliation."
            });
        }

        var decisionBy = ReconciliationUserIdentity.GetActor(user);
        if (decisionBy is null)
        {
            return Results.Forbid();
        }

        var batchToApprove = historyRepository.GetById(id);
        if (batchToApprove is not null &&
            !string.IsNullOrWhiteSpace(batchToApprove.InitiatedBy) &&
            string.Equals(batchToApprove.InitiatedBy, decisionBy, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new ReconciliationErrorResponse
            {
                Error = "SelfApprovalNotAllowed",
                Message = "İşlemi başlatan kullanıcı kendi mutabakatını onaylayamaz veya reddedemez."
            });
        }

        using var transaction = serviceProvider
            .GetService<ReconciliationDbContext>()?
            .Database.BeginTransaction();
        var result = historyRepository.DecideApproval(
            id,
            request.Decision,
            decisionBy,
            comment);

        if (result.Outcome == ReconciliationApprovalDecisionOutcome.Updated &&
            result.Batch is not null)
        {
            auditRepository.Add(
                request.Decision == ReconciliationApprovalDecision.Approve
                    ? ReconciliationAuditAction.ReconciliationApproved
                    : ReconciliationAuditAction.ReconciliationRejected,
                decisionBy,
                ReconciliationAuditResourceType.ReconciliationBatch,
                id.ToString(),
                new { ApprovalStatus = ReconciliationApprovalStatus.Pending },
                new
                {
                    result.Batch.ApprovalStatus,
                    result.Batch.DecisionBy,
                    result.Batch.DecisionAt,
                    result.Batch.DecisionComment
                });
            transaction?.Commit();

            return Results.Ok(result.Batch.ToDetailResponse());
        }

        return result.Outcome switch
        {
            ReconciliationApprovalDecisionOutcome.NotFound =>
                Results.NotFound(new ReconciliationErrorResponse
                {
                    Error = "BatchNotFound",
                    Message = $"Reconciliation batch '{id}' was not found."
                }),
            ReconciliationApprovalDecisionOutcome.BatchNotCompleted =>
                Results.Conflict(new ReconciliationErrorResponse
                {
                    Error = "BatchNotCompleted",
                    Message = "Only completed reconciliations can be approved or rejected."
                }),
            _ => Results.Conflict(new ReconciliationErrorResponse
            {
                Error = "ApprovalAlreadyDecided",
                Message = "This reconciliation already has a final approval decision."
            })
        };
    }

    private static IResult ExportDifferences(
        Guid id,
        IReconciliationHistoryRepository historyRepository,
        IReconciliationExcelReportExporter reportExporter)
    {
        var batch = historyRepository.GetById(id);
        if (batch is null)
        {
            return Results.NotFound(new ReconciliationErrorResponse
            {
                Error = "ReconciliationBatchNotFound",
                Message = $"Reconciliation batch '{id}' was not found."
            });
        }

        var report = reportExporter.ExportDifferences(batch);
        var fileName = $"reconciliation-{id:N}-differences.xlsx";

        return Results.File(
            report,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static async Task<IResult> CompareAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        IHostEnvironment environment,
        IFormFile? branchFile,
        IFormFile? bankFile,
        ITransactionFileParser parser,
        IReconciliationService reconciliationService,
        IReconciliationHistoryRepository historyRepository,
        IOptions<ReconciliationUploadOptions> uploadOptions,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var initiatedBy = GetInitiator(request, user, environment);
        if (initiatedBy is null)
        {
            return Results.Unauthorized();
        }
        var branchFileError = ValidateUploadedFile(
            branchFile,
            fieldName: "branchFile",
            missingError: "MissingBranchFile",
            allowedFileExtensions: uploadOptions.Value.AllowedFileExtensions,
            maxFileSizeBytes: uploadOptions.Value.MaxCsvFileSizeBytes);
        if (branchFileError is not null)
        {
            return Results.BadRequest(branchFileError);
        }

        var bankFileError = ValidateUploadedFile(
            bankFile,
            fieldName: "bankFile",
            missingError: "MissingBankFile",
            allowedFileExtensions: uploadOptions.Value.AllowedFileExtensions,
            maxFileSizeBytes: uploadOptions.Value.MaxCsvFileSizeBytes);
        if (bankFileError is not null)
        {
            return Results.BadRequest(bankFileError);
        }

        var synchronousLimit = Math.Min(
            uploadOptions.Value.SynchronousComparisonMaxFileSizeBytes,
            uploadOptions.Value.MaxCsvFileSizeBytes);
        if (branchFile!.Length > synchronousLimit || bankFile!.Length > synchronousLimit)
        {
            return Results.Json(
                new ReconciliationErrorResponse
                {
                    Error = "AsyncComparisonRequired",
                    Message = "Büyük dosyalar arka planda karşılaştırılmalıdır."
                },
                statusCode: StatusCodes.Status409Conflict);
        }

        var branchDisplayName = GetSafeDisplayFileName(branchFile.FileName);
        var bankDisplayName = GetSafeDisplayFileName(bankFile.FileName);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var branchRecords = await parser.ParseAsync(branchFile!, cancellationToken);
            var bankRecords = await parser.ParseAsync(bankFile!, cancellationToken);
            var summary = reconciliationService.Compare(branchRecords, bankRecords);
            stopwatch.Stop();
            var batch = historyRepository.Add(
                branchDisplayName,
                bankDisplayName,
                stopwatch.ElapsedMilliseconds,
                summary,
                initiatedBy: initiatedBy);

            logger.LogInformation(
                "Compared reconciliation files. BatchId={BatchId}, BranchFile={BranchFile}, BankFile={BankFile}, DurationMs={DurationMs}, Matched={MatchedCount}, Mismatches={MismatchCount}, OnlyInBranch={OnlyInBranchCount}, OnlyInBank={OnlyInBankCount}",
                batch.Id,
                branchDisplayName,
                bankDisplayName,
                batch.ProcessingDurationMilliseconds,
                summary.MatchedCount,
                summary.MismatchCount,
                summary.OnlyInBranchCount,
                summary.OnlyInBankCount);

            return Results.Ok(batch.ToResponse());
        }
        catch (CsvTransactionFileParseException exception)
        {
            stopwatch.Stop();
            var failedBatch = historyRepository.AddFailed(
                branchDisplayName,
                bankDisplayName,
                stopwatch.ElapsedMilliseconds,
                "InvalidCsvFile",
                exception.Message,
                initiatedBy: initiatedBy);

            logger.LogWarning(
                exception,
                "CSV validation failed while comparing reconciliation files. BatchId={BatchId}, BranchFile={BranchFile}, BankFile={BankFile}, DurationMs={DurationMs}, RowNumber={RowNumber}",
                failedBatch.Id,
                branchDisplayName,
                bankDisplayName,
                failedBatch.ProcessingDurationMilliseconds,
                exception.RowNumber);

            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidCsvFile",
                Message = exception.Message,
                RowNumber = exception.RowNumber,
                ColumnName = exception.ColumnName
            });
        }
        catch (DuplicateTransactionKeyException exception)
        {
            stopwatch.Stop();
            var failedBatch = historyRepository.AddFailed(
                branchDisplayName,
                bankDisplayName,
                stopwatch.ElapsedMilliseconds,
                "DuplicateTransactionKey",
                exception.Message,
                initiatedBy: initiatedBy);

            logger.LogWarning(
                exception,
                "Duplicate transaction key found while comparing reconciliation files. BatchId={BatchId}, BranchFile={BranchFile}, BankFile={BankFile}, DurationMs={DurationMs}, SourceName={SourceName}, MatchingKey={MatchingKey}",
                failedBatch.Id,
                branchDisplayName,
                bankDisplayName,
                failedBatch.ProcessingDurationMilliseconds,
                exception.SourceName,
                exception.MatchingKey);

            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "DuplicateTransactionKey",
                Message = exception.Message,
                SourceName = exception.SourceName,
                MatchingKey = exception.MatchingKey
            });
        }
    }

    private static async Task<IResult> QueueFilesComparisonAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        IHostEnvironment environment,
        IReconciliationHistoryRepository historyRepository,
        IReconciliationTemporaryFileStore temporaryFileStore,
        IReconciliationMultipartUploadReader multipartUploadReader,
        ReconciliationFileJobQueue jobQueue,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var initiatedBy = GetInitiator(request, user, environment);
        if (initiatedBy is null)
        {
            return Results.Unauthorized();
        }

        var batchId = Guid.NewGuid();
        ReconciliationStreamedUpload upload;

        try
        {
            upload = await multipartUploadReader.ReadAsync(request, batchId, cancellationToken);
        }
        catch (ReconciliationMultipartUploadException exception)
        {
            await temporaryFileStore.DeleteAsync(batchId, CancellationToken.None);
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = exception.ErrorCode,
                Message = exception.Message
            });
        }
        catch (OperationCanceledException)
        {
            await temporaryFileStore.DeleteAsync(batchId, CancellationToken.None);
            throw;
        }
        catch (ReconciliationTemporaryFileException exception)
        {
            await temporaryFileStore.DeleteAsync(batchId, CancellationToken.None);
            logger.LogError(exception, "Could not stream uploaded reconciliation files. BatchId={BatchId}", batchId);
            return Results.Json(
                new ReconciliationErrorResponse
                {
                    Error = "TemporaryStorageUnavailable",
                    Message = exception.Message
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        ReconciliationBatch batch;
        try
        {
            batch = historyRepository.AddQueued(
                upload.BranchFileName,
                upload.BankFileName,
                ReconciliationInputType.UploadedFiles,
                batchId,
                temporaryFileStore.StorageKey,
                initiatedBy);
        }
        catch
        {
            await temporaryFileStore.DeleteAsync(batchId, CancellationToken.None);
            throw;
        }

        _ = jobQueue.TryQueue(batch.Id);

        return Results.Accepted(
            $"/api/reconciliations/{batch.Id}",
            new ReconciliationJobAcceptedResponse
            {
                BatchId = batch.Id,
                Status = ReconciliationBatchStatus.Queued,
                InputType = ReconciliationInputType.UploadedFiles
            });
    }

    private static async Task<IResult> CompareDatabaseSourcesAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        IHostEnvironment environment,
        IReconciliationDatabaseSourceReader databaseSourceReader,
        IReconciliationSourceRepository sourceRepository,
        IReconciliationService reconciliationService,
        IReconciliationHistoryRepository historyRepository,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var initiatedBy = GetInitiator(request, user, environment);
        if (initiatedBy is null)
        {
            return Results.Unauthorized();
        }

        const string branchSourceCode = "BRANCH";
        const string bankSourceCode = "BANK";
        const string branchSourceName = "database:BRANCH";
        const string bankSourceName = "database:BANK";
        var sources = sourceRepository.GetAll();
        var inactiveSource = new[] { branchSourceCode, bankSourceCode }
            .FirstOrDefault(code => !sources.Any(source =>
                source.IsActive && string.Equals(source.Code, code, StringComparison.OrdinalIgnoreCase)));
        if (inactiveSource is not null)
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InactiveReconciliationSource",
                Message = $"Reconciliation source '{inactiveSource}' must be active.",
                SourceName = inactiveSource
            });
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var branchReadTask = databaseSourceReader.ReadAsync(branchSourceCode, cancellationToken);
            var bankReadTask = databaseSourceReader.ReadAsync(bankSourceCode, cancellationToken);
            await Task.WhenAll(branchReadTask, bankReadTask);

            var summary = reconciliationService.Compare(
                await branchReadTask,
                await bankReadTask);
            stopwatch.Stop();
            var batch = historyRepository.Add(
                branchSourceName,
                bankSourceName,
                stopwatch.ElapsedMilliseconds,
                summary,
                ReconciliationInputType.DatabaseSources,
                initiatedBy);

            logger.LogInformation(
                "Compared reconciliation database sources. BatchId={BatchId}, DurationMs={DurationMs}, Matched={MatchedCount}, Mismatches={MismatchCount}, OnlyInBranch={OnlyInBranchCount}, OnlyInBank={OnlyInBankCount}",
                batch.Id,
                batch.ProcessingDurationMilliseconds,
                summary.MatchedCount,
                summary.MismatchCount,
                summary.OnlyInBranchCount,
                summary.OnlyInBankCount);

            return Results.Ok(batch.ToResponse());
        }
        catch (ReconciliationDatabaseSourceException exception)
        {
            stopwatch.Stop();
            var failedBatch = historyRepository.AddFailed(
                branchSourceName,
                bankSourceName,
                stopwatch.ElapsedMilliseconds,
                "DatabaseSourceReadFailed",
                exception.Message,
                ReconciliationInputType.DatabaseSources,
                initiatedBy);

            logger.LogWarning(
                exception,
                "Database source read failed. BatchId={BatchId}, SourceCode={SourceCode}, DurationMs={DurationMs}",
                failedBatch.Id,
                exception.SourceCode,
                failedBatch.ProcessingDurationMilliseconds);

            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "DatabaseSourceReadFailed",
                Message = exception.Message,
                SourceName = exception.SourceCode
            });
        }
        catch (DuplicateTransactionKeyException exception)
        {
            stopwatch.Stop();
            var failedBatch = historyRepository.AddFailed(
                branchSourceName,
                bankSourceName,
                stopwatch.ElapsedMilliseconds,
                "DuplicateTransactionKey",
                exception.Message,
                ReconciliationInputType.DatabaseSources,
                initiatedBy);

            logger.LogWarning(
                exception,
                "Duplicate transaction key found while comparing database sources. BatchId={BatchId}, DurationMs={DurationMs}, SourceName={SourceName}, MatchingKey={MatchingKey}",
                failedBatch.Id,
                failedBatch.ProcessingDurationMilliseconds,
                exception.SourceName,
                exception.MatchingKey);

            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "DuplicateTransactionKey",
                Message = exception.Message,
                SourceName = exception.SourceName,
                MatchingKey = exception.MatchingKey
            });
        }
    }

    private static IResult QueueDatabaseSourcesComparison(
        HttpRequest request,
        ClaimsPrincipal user,
        IHostEnvironment environment,
        IReconciliationSourceRepository sourceRepository,
        IReconciliationHistoryRepository historyRepository,
        ReconciliationDatabaseJobQueue jobQueue)
    {
        var initiatedBy = GetInitiator(request, user, environment);
        if (initiatedBy is null)
        {
            return Results.Unauthorized();
        }

        const string branchSourceName = "database:BRANCH";
        const string bankSourceName = "database:BANK";
        var sources = sourceRepository.GetAll();
        var inactiveSource = new[] { "BRANCH", "BANK" }
            .FirstOrDefault(code => !sources.Any(source =>
                source.IsActive && string.Equals(source.Code, code, StringComparison.OrdinalIgnoreCase)));
        if (inactiveSource is not null)
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InactiveReconciliationSource",
                Message = $"Reconciliation source '{inactiveSource}' must be active.",
                SourceName = inactiveSource
            });
        }

        var batch = historyRepository.AddQueued(
            branchSourceName,
            bankSourceName,
            ReconciliationInputType.DatabaseSources,
            initiatedBy: initiatedBy);
        _ = jobQueue.TryQueue(batch.Id);

        return Results.Accepted(
            $"/api/reconciliations/{batch.Id}",
            new ReconciliationJobAcceptedResponse
            {
                BatchId = batch.Id,
                Status = ReconciliationBatchStatus.Queued,
                InputType = ReconciliationInputType.DatabaseSources
            });
    }

    private static string GetSafeDisplayFileName(string fileName)
    {
        var normalized = fileName.Replace('\\', '/');
        var displayName = normalized[(normalized.LastIndexOf('/') + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "uploaded-file";
        }

        return displayName.Length <= 260 ? displayName : displayName[..260];
    }

    private static string? GetInitiator(
        HttpRequest request,
        ClaimsPrincipal user,
        IHostEnvironment environment)
    {
        var authenticatedActor = ReconciliationUserIdentity.GetActor(user);
        if (authenticatedActor is not null)
        {
            return authenticatedActor;
        }

        if (!environment.IsEnvironment("Testing"))
        {
            return null;
        }

        var testActor = request.Headers["X-Reconciliation-Initiator"].ToString().Trim();
        if (testActor.Length is > 0 and <= 200)
        {
            return testActor;
        }

        return environment.IsEnvironment("Testing") ? "test-operator" : null;
    }

    private static ReconciliationErrorResponse? ValidateUploadedFile(
        IFormFile? file,
        string fieldName,
        string missingError,
        IReadOnlyCollection<string> allowedFileExtensions,
        long maxFileSizeBytes)
    {
        if (file is null || file.Length == 0)
        {
            return new ReconciliationErrorResponse
            {
                Error = missingError,
                Message = $"{fieldName} is required and cannot be empty."
            };
        }

        if (!allowedFileExtensions.Contains(
                Path.GetExtension(file.FileName),
                StringComparer.OrdinalIgnoreCase))
        {
            return new ReconciliationErrorResponse
            {
                Error = "InvalidFileExtension",
                Message = $"{fieldName} must use one of these extensions: {string.Join(", ", allowedFileExtensions)}."
            };
        }

        if (file.Length > maxFileSizeBytes)
        {
            return new ReconciliationErrorResponse
            {
                Error = "FileTooLarge",
                Message = $"{fieldName} must be {maxFileSizeBytes} bytes or smaller."
            };
        }

        return null;
    }
}
