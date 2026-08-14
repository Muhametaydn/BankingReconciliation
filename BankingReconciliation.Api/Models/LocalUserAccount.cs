namespace BankingReconciliation.Api.Models;

public sealed class LocalUserAccount
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; }
    public required string PasswordHash { get; set; }
    public LocalUserRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
