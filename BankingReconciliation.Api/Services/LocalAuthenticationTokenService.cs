using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BankingReconciliation.Api.Services;

public sealed class LocalAuthenticationTokenService
{
    private readonly ReconciliationAuthenticationOptions _options;
    private readonly TimeProvider _timeProvider;

    public LocalAuthenticationTokenService(
        IOptions<ReconciliationAuthenticationOptions> options,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public LocalAuthenticationToken CreateToken(LocalUserAccount user)
    {
        if (_options.LocalSigningKey.Length < 32)
        {
            throw new InvalidOperationException("Local authentication signing key is not configured.");
        }

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddHours(8);
        var claims = new List<Claim>
        {
            new("sub", user.Username),
            new(_options.NameClaimType, user.Username),
            new(_options.RoleClaimType, GetConfiguredRole(user.Role))
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.LocalSigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new LocalAuthenticationToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }

    public string GetConfiguredRole(LocalUserRole role) => role switch
    {
        LocalUserRole.Administrator => _options.AdministratorRole,
        LocalUserRole.Approver => _options.ApproverRole,
        _ => _options.OperatorRole
    };
}

public sealed record LocalAuthenticationToken(string Value, DateTimeOffset ExpiresAt);
