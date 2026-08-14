using System.Text.Json;
using BankingReconciliation.Api.Models;
using BankingReconciliation.Api.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BankingReconciliation.Api.Services;

public sealed class LocalUserAccountStore : ILocalUserAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _storePath;
    private readonly PasswordHasher<LocalUserAccount> _passwordHasher = new();

    public LocalUserAccountStore(
        IHostEnvironment environment,
        IOptions<ReconciliationAuthenticationOptions> options)
    {
        _storePath = !string.IsNullOrWhiteSpace(options.Value.LocalUserStorePath)
            ? Path.GetFullPath(options.Value.LocalUserStorePath)
            : environment.IsEnvironment("Testing")
            ? Path.Combine(
                Path.GetTempPath(),
                $"BankingReconciliation-Users-{Environment.ProcessId}-{Guid.NewGuid():N}.json")
            : Path.Combine(
                environment.ContentRootPath,
                ".local-data",
                "users.json");
    }

    public LocalUserRegistrationResult Register(string username, string password)
    {
        lock (_sync)
        {
            var users = ReadUsers();
            var normalizedUsername = NormalizeUsername(username);
            if (users.Any(user => user.NormalizedUsername == normalizedUsername))
            {
                return new LocalUserRegistrationResult(null, false, true);
            }

            var isFirstAdministrator = users.Count == 0;
            var user = new LocalUserAccount
            {
                Id = Guid.NewGuid(),
                Username = username.Trim(),
                NormalizedUsername = normalizedUsername,
                PasswordHash = string.Empty,
                Role = isFirstAdministrator
                    ? LocalUserRole.Administrator
                    : LocalUserRole.Operator,
                CreatedAt = DateTimeOffset.UtcNow
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            users.Add(user);
            WriteUsers(users);
            return new LocalUserRegistrationResult(user, isFirstAdministrator, false);
        }
    }

    public LocalUserAccount? ValidateCredentials(string username, string password)
    {
        lock (_sync)
        {
            var users = ReadUsers();
            var user = users.SingleOrDefault(candidate =>
                candidate.NormalizedUsername == NormalizeUsername(username));
            if (user is null)
            {
                return null;
            }

            var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (result == PasswordVerificationResult.Failed)
            {
                return null;
            }

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, password);
                WriteUsers(users);
            }

            return user;
        }
    }

    public LocalUserAccount? GetByUsername(string username)
    {
        lock (_sync)
        {
            var normalizedUsername = NormalizeUsername(username);
            return ReadUsers().SingleOrDefault(user => user.NormalizedUsername == normalizedUsername);
        }
    }

    public IReadOnlyList<LocalUserAccount> GetAll()
    {
        lock (_sync)
        {
            return ReadUsers()
                .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public LocalUserRoleUpdateResult UpdateRole(Guid userId, LocalUserRole role)
    {
        lock (_sync)
        {
            var users = ReadUsers();
            var user = users.SingleOrDefault(candidate => candidate.Id == userId);
            if (user is null)
            {
                return new LocalUserRoleUpdateResult(LocalUserRoleUpdateOutcome.NotFound, null);
            }

            if (user.Role == LocalUserRole.Administrator &&
                role != LocalUserRole.Administrator &&
                users.Count(candidate => candidate.Role == LocalUserRole.Administrator) == 1)
            {
                return new LocalUserRoleUpdateResult(LocalUserRoleUpdateOutcome.LastAdministrator, user);
            }

            user.Role = role;
            WriteUsers(users);
            return new LocalUserRoleUpdateResult(LocalUserRoleUpdateOutcome.Updated, user);
        }
    }

    private List<LocalUserAccount> ReadUsers()
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        var json = File.ReadAllText(_storePath);
        return JsonSerializer.Deserialize<List<LocalUserAccount>>(json, JsonOptions) ?? [];
    }

    private void WriteUsers(List<LocalUserAccount> users)
    {
        var directory = Path.GetDirectoryName(_storePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"users-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(users, JsonOptions));
            File.Move(temporaryPath, _storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string NormalizeUsername(string username) => username.Trim().ToUpperInvariant();
}
