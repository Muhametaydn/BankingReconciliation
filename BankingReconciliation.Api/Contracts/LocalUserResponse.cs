namespace BankingReconciliation.Api.Contracts;

public sealed class LocalUserResponse
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
