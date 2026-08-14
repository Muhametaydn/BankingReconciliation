using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.TryGetValue("X-Test-Anonymous", out var anonymous) &&
            string.Equals(anonymous.ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var isManagementRequest = HttpMethods.IsPut(Request.Method) ||
            Request.Path.StartsWithSegments("/api/reconciliation-audit-events");
        var actor = Request.Headers.TryGetValue("X-Test-Actor", out var actorHeader) &&
            !string.IsNullOrWhiteSpace(actorHeader)
                ? actorHeader.ToString().Split(',')[0].Trim()
                : isManagementRequest ? "test-administrator" : null;
        if (actor is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("sub", actor),
            new("name", actor)
        };

        if (Request.Headers.TryGetValue("X-Test-Permission", out var permission) &&
            !string.IsNullOrWhiteSpace(permission))
        {
            claims.AddRange(permission
                .SelectMany(value => value?.Split(',') ?? [])
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Select(value => new Claim("permission", value)));
        }
        else if (isManagementRequest)
        {
            claims.Add(new Claim("permission", "reconciliation.manage"));
        }

        if (Request.Headers.TryGetValue("X-Test-Role", out var role) &&
            !string.IsNullOrWhiteSpace(role))
        {
            claims.AddRange(role
                .SelectMany(value => value?.Split(',') ?? [])
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Select(value => new Claim("role", value)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "name", "role");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
