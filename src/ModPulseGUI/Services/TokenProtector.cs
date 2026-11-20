using System.Security.Cryptography;
using System.Text;

namespace ModPulse.Gui.Services;

/// <summary>
/// Keeps the GitHub token out of plain text in the settings file: Windows DPAPI, readable only by the same Windows
/// user on the same PC.
/// </summary>
public static class TokenProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ModPulse.GitHubToken");

    /// <summary>The token encrypted for the current Windows user, as Base64; null for an empty token.</summary>
    public static string? Protect(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>The saved token, or null when there is none or it can't be decrypted (another user or PC).</summary>
    public static string? Unprotect(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken))
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
