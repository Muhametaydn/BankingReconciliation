namespace BankingReconciliation.Api.Contracts;

public sealed class RegisterLocalUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
