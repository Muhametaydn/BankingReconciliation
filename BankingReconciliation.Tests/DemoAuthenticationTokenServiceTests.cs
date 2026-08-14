using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BankingReconciliation.Api.Options;
using BankingReconciliation.Api.Services;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Tests;

public class DemoAuthenticationTokenServiceTests
{
    [Fact]
    public void CreateToken_ContainsActorAndRequestedPermission()
    {
        var options = Options.Create(new ReconciliationAuthenticationOptions
        {
            DemoSigningKey = "local-demo-only-signing-key-2026-change-before-production"
        });
        var service = new DemoAuthenticationTokenService(options, TimeProvider.System);

        var token = service.CreateToken(
            "approver-1",
            new Claim("permission", "reconciliation.approve"));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("approver-1", jwt.Claims.Single(claim => claim.Type == "sub").Value);
        Assert.Contains(jwt.Claims, claim =>
            claim.Type == "permission" && claim.Value == "reconciliation.approve");
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
    }
}
