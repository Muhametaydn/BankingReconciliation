using System.Text.Json;
using BankingReconciliation.Api.Contracts;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Security;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Endpoints;

public static class ReconciliationAuditEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/reconciliation-audit-events", GetAuditEvents)
            .WithName("GetReconciliationAuditEvents")
            .RequireAuthorization(ReconciliationAuthorizationPolicies.Administrator)
            .Produces<List<ReconciliationAuditEventResponse>>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapGet("/api/reconciliation-audit-retention/status", GetRetentionStatus)
            .WithName("GetReconciliationAuditRetentionStatus")
            .RequireAuthorization(ReconciliationAuthorizationPolicies.Administrator)
            .Produces<ReconciliationAuditRetentionStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapGet("/api/health/audit-retention", GetRetentionHealth)
            .WithName("GetReconciliationAuditRetentionHealth")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static IResult GetRetentionStatus(
        ReconciliationAuditRetentionHealthEvaluator healthEvaluator,
        IOptions<ReconciliationAuditRetentionOptions> options)
    {
        var configuration = options.Value;
        var health = healthEvaluator.Evaluate();
        var storage = health.Storage;
        var execution = health.Execution;

        return Results.Ok(new ReconciliationAuditRetentionStatusResponse
        {
            Status = health.Status,
            Enabled = configuration.Enabled,
            ImmutableArchiveEnabled = health.ImmutableArchiveEnabled,
            HotRetentionDays = configuration.HotRetentionDays,
            ArchiveRetentionDays = configuration.ArchiveRetentionDays,
            BatchSize = configuration.BatchSize,
            HotEventCount = storage.HotEventCount,
            ArchivedEventCount = storage.ArchivedEventCount,
            PendingExternalArchiveCount = health.PendingExternalArchiveCount,
            OldestPendingExternalArchiveAt = health.OldestPendingExternalArchiveAt,
            LastStartedAt = execution.LastStartedAt,
            LastSucceededAt = execution.LastSucceededAt,
            LastFailedAt = execution.LastFailedAt,
            LastArchivedCount = execution.LastArchivedCount,
            LastPurgedCount = execution.LastPurgedCount,
            LastExternalArchivedCount = execution.LastExternalArchivedCount,
            Alerting = health.Alerts.Count > 0,
            Alerts = health.Alerts
        });
    }

    private static IResult GetRetentionHealth(
        ReconciliationAuditRetentionHealthEvaluator healthEvaluator)
    {
        var health = healthEvaluator.Evaluate();
        var response = new
        {
            Application = "Banking Reconciliation API",
            Status = health.Status,
            Alerts = health.Alerts
        };

        return Results.Json(
            response,
            statusCode: health.Status == "Degraded"
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status200OK);
    }

    private static IResult GetAuditEvents(
        IReconciliationAuditRepository auditRepository,
        HttpResponse response,
        string? actor,
        DateTimeOffset? from,
        DateTimeOffset? to,
        ReconciliationAuditAction? action,
        ReconciliationAuditResourceType? resourceType,
        int skip = 0,
        int take = 50)
    {
        if (skip < 0 || take is < 1 or > 200 || actor?.Length > 200 ||
            (from is not null && to is not null && from > to))
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidAuditQuery",
                Message = "Audit paging, actor, or date range is invalid."
            });
        }

        var query = new ReconciliationAuditQuery
        {
            Actor = actor,
            From = from,
            To = to,
            Action = action,
            ResourceType = resourceType,
            Skip = skip,
            Take = take
        };
        response.Headers.Append("X-Total-Count", auditRepository.Count(query).ToString());

        return Results.Ok(auditRepository
            .GetAll(query)
            .Select(ToResponse)
            .ToList());
    }

    private static ReconciliationAuditEventResponse ToResponse(ReconciliationAuditEvent auditEvent)
    {
        return new ReconciliationAuditEventResponse
        {
            Id = auditEvent.Id,
            CreatedAt = auditEvent.CreatedAt,
            Actor = auditEvent.Actor,
            Action = auditEvent.Action,
            ResourceType = auditEvent.ResourceType,
            ResourceId = auditEvent.ResourceId,
            BeforeState = ParseState(auditEvent.BeforeStateJson),
            AfterState = ParseState(auditEvent.AfterStateJson),
            ArchivedAt = auditEvent.ArchivedAt,
            IntegrityHash = auditEvent.IntegrityHash,
            IntegrityVerified = auditEvent.IntegrityVerified,
            ExternalArchivedAt = auditEvent.ExternalArchivedAt,
            ExternalArchiveKey = auditEvent.ExternalArchiveKey
        };
    }

    private static JsonElement? ParseState(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<JsonElement>(json);
    }
}
