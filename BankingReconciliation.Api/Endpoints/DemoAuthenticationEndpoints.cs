using System.Security.Claims;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Endpoints;

public static class DemoAuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapDemoAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/demo-auth/session", CreateDemoSession)
            .WithName("CreateDemoAuthenticationSession")
            .ExcludeFromDescription();
        app.MapGet("/api/demo-files/{fileKey}", DownloadDemoFile)
            .WithName("DownloadDemoReconciliationFile")
            .ExcludeFromDescription();
        return app;
    }

    private static IResult DownloadDemoFile(string fileKey, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return Results.NotFound();
        }

        var fileName = fileKey switch
        {
            "comparison-file-1" => "comparison-file-1.csv",
            "comparison-file-2" => "comparison-file-2.csv",
            "invalid-comparison-file" => "invalid-comparison-file.csv",
            _ => null
        };
        if (fileName is null)
        {
            return Results.NotFound();
        }

        var filePath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "demo-data", fileName));
        return File.Exists(filePath)
            ? Results.File(filePath, "text/csv; charset=utf-8", fileName)
            : Results.NotFound();
    }

    private static IResult CreateDemoSession(
        IHostEnvironment environment,
        DemoAuthenticationTokenService tokenService,
        IOptions<ReconciliationAuthenticationOptions> options)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return Results.NotFound();
        }

        if (options.Value.DemoSigningKey.Length < 32)
        {
            return Results.Problem("Yerel demo girişi yapılandırılmamış.", statusCode: 503);
        }

        return Results.Ok(new
        {
            operatorUser = "operator-1",
            operatorToken = tokenService.CreateToken("operator-1"),
            approverUser = "approver-1",
            approverToken = tokenService.CreateToken(
                "approver-1",
                new Claim(options.Value.PermissionClaimType, options.Value.ApproverPermission)),
            administratorUser = "administrator-1",
            administratorToken = tokenService.CreateToken(
                "administrator-1",
                new Claim(options.Value.PermissionClaimType, options.Value.AdministratorPermission))
        });
    }
}
