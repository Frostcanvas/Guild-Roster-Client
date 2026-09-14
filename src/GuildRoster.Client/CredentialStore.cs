using System.Security.Cryptography;
using System.Text;

namespace GuildRoster.Client;

internal static class CredentialStore
{
    public static bool HasToken => !string.IsNullOrWhiteSpace(LoadToken());

    public static void SaveToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        AppPaths.EnsureCreated();

        var plaintext = Encoding.UTF8.GetBytes(token.Trim());
        var protectedBytes = ProtectedData.Protect(
            plaintext,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(AppPaths.CredentialPath, protectedBytes);
        CryptographicOperations.ZeroMemory(plaintext);
    }

    public static string? LoadToken()
    {
        if (!File.Exists(AppPaths.CredentialPath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(AppPaths.CredentialPath);
            var plaintext = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            try
            {
                var token = Encoding.UTF8.GetString(plaintext).Trim();
                return token.Length == 0 ? null : token;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(AppPaths.CredentialPath))
            {
                File.Delete(AppPaths.CredentialPath);
            }
        }
        catch (IOException)
        {
            // The user can retry pairing after the temporary file lock is released.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the existing protected credential if Windows denies deletion.
        }
    }
}
