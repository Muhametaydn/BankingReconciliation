using System.Security.Claims;

namespace BankingReconciliation.Api.Security;

public static class ReconciliationUserIdentity
{
    public static string? GetActor(ClaimsPrincipal user)
    {
        var actor = user.FindFirst("sub")?.Value ??
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            user.Identity?.Name;

        return string.IsNullOrWhiteSpace(actor) || actor.Length > 200
            ? null
            : actor.Trim();
    }
}
