using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public interface ILocalUserAccountStore
{
    LocalUserRegistrationResult Register(string username, string password);
    LocalUserAccount? ValidateCredentials(string username, string password);
    LocalUserAccount? GetByUsername(string username);
    IReadOnlyList<LocalUserAccount> GetAll();
    LocalUserRoleUpdateResult UpdateRole(Guid userId, LocalUserRole role);
}

public sealed record LocalUserRegistrationResult(
    LocalUserAccount? User,
    bool IsFirstAdministrator,
    bool UsernameAlreadyExists);

public enum LocalUserRoleUpdateOutcome
{
    Updated,
    NotFound,
    LastAdministrator
}

public sealed record LocalUserRoleUpdateResult(
    LocalUserRoleUpdateOutcome Outcome,
    LocalUserAccount? User);
