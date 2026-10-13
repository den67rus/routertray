using System.Security.Cryptography;
using System.Text;

namespace RouterTray;

internal static class SecretProtector
{
    private static readonly byte[] PasswordEntropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("RouterTray.AppSettings.Password.v1"));
    private static readonly byte[] AccessTokenEntropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("RouterTray.AppSettings.AccessToken.v1"));

    public static string Protect(string value)
    {
        return Protect(value, PasswordEntropy);
    }

    public static string ProtectAccessToken(string value)
    {
        return Protect(value, AccessTokenEntropy);
    }

    public static string Unprotect(string value)
    {
        return Unprotect(value, PasswordEntropy, "password");
    }

    public static string UnprotectAccessToken(string value)
    {
        return Unprotect(value, AccessTokenEntropy, "access token");
    }

    private static string Protect(string value, byte[] optionalEntropy)
    {
        ArgumentNullException.ThrowIfNull(value);

        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            var protectedBytes = ProtectedData.Protect(
                plaintext,
                optionalEntropy,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string Unprotect(string value, byte[] optionalEntropy, string secretName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        byte[] protectedBytes;
        try
        {
            protectedBytes = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"Protected {secretName} is not valid Base64.", ex);
        }

        try
        {
            var plaintext = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy,
                DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException(
                $"Protected {secretName} cannot be decrypted for the current Windows user.",
                ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }
}
