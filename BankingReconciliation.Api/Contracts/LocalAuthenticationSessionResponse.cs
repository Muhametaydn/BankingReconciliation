namespace BankingReconciliation.Api.Contracts;

public sealed class LocalAuthenticationSessionResponse
{
    public required string AccessToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public required LocalUserResponse User { get; set; }
    public bool IsFirstAdministrator { get; set; }
}
