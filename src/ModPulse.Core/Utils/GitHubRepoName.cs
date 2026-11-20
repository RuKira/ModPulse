using System.Text.RegularExpressions;

namespace ModPulse.Utils;

/// <summary>Reads a GitHub repository from what people paste: <c>owner/repo</c> or any github.com URL in it.</summary>
public static partial class GitHubRepoName
{
    // GitHub owner names: letters, digits and single hyphens. Repo names: letters, digits, '.', '_' and '-'.
    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex OwnerRepo();

    /// <summary>
    /// Parses <c>owner/repo</c>, <c>github.com/owner/repo</c> or a full URL such as
    /// <c>https://github.com/owner/repo/releases/tag/v1.0</c> into <c>owner/repo</c>.
    /// </summary>
    public static bool TryParse(string? input, out string ownerRepo)
    {
        ownerRepo = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();

        var hostIndex = text.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
        if (hostIndex >= 0)
        {
            text = text[(hostIndex + "github.com/".Length)..];
        }
        else if (text.Contains("://", StringComparison.Ordinal) || text.Contains(' '))
        {
            // A URL for some other site, or a sentence.
            return false;
        }

        var parts = text.Split(['/', '?', '#'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || (hostIndex < 0 && parts.Length > 2))
        {
            return false;
        }

        var repo = parts[1];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }

        var candidate = parts[0] + "/" + repo;
        if (!OwnerRepo().IsMatch(candidate) || repo is "." or "..")
        {
            return false;
        }

        ownerRepo = candidate;
        return true;
    }
}
