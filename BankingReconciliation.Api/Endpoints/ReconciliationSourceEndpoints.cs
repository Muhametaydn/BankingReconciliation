using System.Security.Claims;
using BankingReconciliation.Api.Contracts;
using BankingReconciliation.Api.Data;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Security;
using BankingReconciliation.Api.Services;

namespace BankingReconciliation.Api.Endpoints;

public static class ReconciliationSourceEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationSourceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/reconciliation-sources", GetSources)
            .WithName("GetReconciliationSources")
            .Produces<List<ReconciliationSourceResponse>>(StatusCodes.Status200OK);

        app.MapPut("/api/reconciliation-sources/{id:guid}", UpdateSource)
            .WithName("UpdateReconciliationSource")
            .RequireAuthorization(ReconciliationAuthorizationPolicies.Administrator)
            .Produces<ReconciliationSourceResponse>(StatusCodes.Status200OK)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ReconciliationErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    private static IResult GetSources(
        IReconciliationSourceRepository sourceRepository,
        IReconciliationDatabaseSourceConfiguration databaseSourceConfiguration)
    {
        var sources = sourceRepository
            .GetAll()
            .Select(source => new ReconciliationSourceResponse
            {
                Id = source.Id,
                Type = source.Type,
                Code = source.Code,
                DisplayName = source.DisplayName,
                Description = source.Description,
                IsActive = source.IsActive,
                IsDatabaseConfigured = databaseSourceConfiguration.IsConfigured(source.Code)
            })
            .ToList();

        return Results.Ok(sources);
    }

    private static IResult UpdateSource(
        Guid id,
        UpdateReconciliationSourceRequest request,
        ClaimsPrincipal user,
        IServiceProvider serviceProvider,
        IReconciliationSourceRepository sourceRepository,
        IReconciliationAuditRepository auditRepository,
        IReconciliationDatabaseSourceConfiguration databaseSourceConfiguration)
    {
        var displayName = request.DisplayName.Trim();
        var description = request.Description.Trim();
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 160 || description.Length > 500)
        {
            return Results.BadRequest(new ReconciliationErrorResponse
            {
                Error = "InvalidReconciliationSource",
                Message = "Display name is required and source text exceeds the allowed length."
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
        var beforeState = sourceRepository.GetAll().SingleOrDefault(source => source.Id == id);
        var source = sourceRepository.Update(id, displayName, description, request.IsActive);
        if (source is null)
        {
            return Results.NotFound(new ReconciliationErrorResponse
            {
                Error = "ReconciliationSourceNotFound",
                Message = $"Reconciliation source '{id}' was not found."
            });
        }

        auditRepository.Add(
            ReconciliationAuditAction.SourceUpdated,
            actor,
            ReconciliationAuditResourceType.ReconciliationSource,
            id.ToString(),
            beforeState,
            source);
        transaction?.Commit();

        return Results.Ok(ToResponse(source, databaseSourceConfiguration));
    }

    private static ReconciliationSourceResponse ToResponse(
        ReconciliationSource source,
        IReconciliationDatabaseSourceConfiguration databaseSourceConfiguration)
    {
        return new ReconciliationSourceResponse
        {
            Id = source.Id,
            Type = source.Type,
            Code = source.Code,
            DisplayName = source.DisplayName,
            Description = source.Description,
            IsActive = source.IsActive,
            IsDatabaseConfigured = databaseSourceConfiguration.IsConfigured(source.Code)
        };
    }
}
