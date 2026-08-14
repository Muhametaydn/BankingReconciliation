using System.Security.Cryptography;

namespace BankingReconciliation.Api.Services;

public static class LocalAuthenticationSigningKeyProvider
{
    public static string GetOrCreate(IHostEnvironment environment)
    {
        if (environment.IsEnvironment("Testing"))
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        }

        var directory = Path.Combine(environment.ContentRootPath, ".local-data");
        var keyPath = Path.Combine(directory, "jwt-signing-key.txt");
        Directory.CreateDirectory(directory);
        if (File.Exists(keyPath))
        {
            var existingKey = File.ReadAllText(keyPath).Trim();
            if (existingKey.Length >= 32)
            {
                return existingKey;
            }
        }

        var generatedKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        File.WriteAllText(keyPath, generatedKey);
        return generatedKey;
    }
}
