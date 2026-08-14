using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BankingReconciliation.Api.Services;

public sealed class DemoAuthenticationTokenService
{
    private readonly ReconciliationAuthenticationOptions _options;
    private readonly TimeProvider _timeProvider;

    public DemoAuthenticationTokenService(
        IOptions<ReconciliationAuthenticationOptions> options,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public string CreateToken(string actor, params Claim[] additionalClaims)
    {
        if (_options.DemoSigningKey.Length < 32)
        {
            throw new InvalidOperationException("Local demo authentication is not configured.");
        }

        var now = _timeProvider.GetUtcNow();
        var claims = new List<Claim>
        {
            new("sub", actor),
            new(_options.NameClaimType, actor)
        };
        claims.AddRange(additionalClaims);

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.DemoSigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddHours(8).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
