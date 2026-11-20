using ModPulse.Models;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;

namespace ModPulse.Services;

/// <summary>What <see cref="GitHubUpdateResolver.ResolveAsync"/> did.</summary>
/// <param name="Checked">Mods checked against a linked repository.</param>
/// <param name="RateLimitedUntil">Set when GitHub's rate limit stopped the checks.</param>
public sealed record GitHubResolveSummary(int Checked, DateTimeOffset? RateLimitedUntil);

/// <summary>
/// Checks off-Forge mods that are linked to a GitHub repository against that repository's latest release. GitHub
/// releases carry no SPT compatibility, so an update is only "a newer release", never a compatibility verdict.
/// </summary>
public static class GitHubUpdateResolver
{
    /// <summary>
    /// Sets <see cref="Mod.GitHub"/> on every linked mod that isn't on the Forge and doesn't belong to another mod
    /// (addons and bundled parts update with their parent). Each repository is asked once per run. When the rate limit
    /// runs out, the remaining mods are marked unknown and no further requests are made.
    /// </summary>
    public static async Task<GitHubResolveSummary> ResolveAsync(
        IReadOnlyList<Mod> mods,
        IGitHubSourceStore store,
        IGitHubReleaseClient client,
        SemanticVersioning.Version sptVersion,
        CancellationToken cancellationToken = default
    )
    {
        var lookups = new Dictionary<string, GitHubLookup>(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset? rateLimitedUntil = null;
        var checkedCount = 0;

        foreach (var mod in mods.Where(m => !m.IsMatched && !m.BelongsToAnotherMod))
        {
            var link = store.Find(mod);
            if (link is null)
            {
                continue;
            }

            if (!lookups.TryGetValue(link.Repo, out var lookup))
            {
                lookup = rateLimitedUntil is not null
                    ? new GitHubLookup(GitHubLookupStatus.RateLimited, RateLimitResetUtc: rateLimitedUntil)
                    : await client.GetLookupSafeAsync(link.Repo, cancellationToken);

                if (lookup.Status == GitHubLookupStatus.RateLimited)
                {
                    rateLimitedUntil ??= lookup.RateLimitResetUtc ?? DateTimeOffset.UtcNow.AddHours(1);
                }

                lookups[link.Repo] = lookup;
            }

            mod.SetGitHub(Evaluate(mod.LocalVersion, link, lookup, sptVersion));
            checkedCount++;
        }

        return new GitHubResolveSummary(checkedCount, rateLimitedUntil);
    }

    private static async Task<GitHubLookup> GetLookupSafeAsync(
        this IGitHubReleaseClient client,
        string repo,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await client.GetLatestAsync(repo, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new GitHubLookup(GitHubLookupStatus.Failed);
        }
    }

    /// <summary>Turns a lookup into the mod's GitHub status.</summary>
    internal static GitHubMatch Evaluate(
        string installedVersion,
        GitHubSource link,
        GitHubLookup lookup,
        SemanticVersioning.Version sptVersion
    )
    {
        GitHubMatch Unknown(string reason, GitHubRelease? release = null)
        {
            return new GitHubMatch(link.Repo, link.Origin, GitHubMatchStatus.Unknown, reason, release);
        }

        switch (lookup.Status)
        {
            case GitHubLookupStatus.NotFound:
                return Unknown("The repository wasn't found on GitHub (renamed, deleted or private).");
            case GitHubLookupStatus.NoReleases:
                return Unknown("The repository has no releases or version tags yet.");
            case GitHubLookupStatus.RateLimited:
                var until = (lookup.RateLimitResetUtc ?? DateTimeOffset.UtcNow.AddHours(1)).ToLocalTime();
                return Unknown($"GitHub's rate limit was reached; try again after {until:HH:mm}.");
            case GitHubLookupStatus.Failed:
            case not GitHubLookupStatus.Found:
                return Unknown("Couldn't reach GitHub to check this repository.");
        }

        var release = lookup.Release!;
        var latest = ReleaseVersion.FromTag(release.Tag);
        if (latest is null)
        {
            return Unknown($"Couldn't read a version from the GitHub tag '{release.Tag}'.", release);
        }

        var installed = ReleaseVersion.FromInstalled(installedVersion);
        if (installed is null)
        {
            return Unknown($"Couldn't read the installed version '{installedVersion}' to compare with {latest}.", release);
        }

        var version = latest.ToString();
        if (latest == installed)
        {
            return new GitHubMatch(link.Repo, link.Origin, GitHubMatchStatus.UpToDate, $"Latest GitHub release is {version}.", release, version);
        }

        if (latest < installed)
        {
            return new GitHubMatch(
                link.Repo,
                link.Origin,
                GitHubMatchStatus.NewerInstalled,
                $"Installed {installedVersion} is newer than GitHub's latest release {version}.",
                release,
                version
            );
        }

        var sptLine = $"{sptVersion.Major}.{sptVersion.Minor}";
        if (SptLayout.IsLegacy(sptVersion) && !MentionsSptLine(release, sptLine))
        {
            return new GitHubMatch(
                link.Repo,
                link.Origin,
                GitHubMatchStatus.HeldBack,
                $"GitHub has {version}, but its release doesn't mention SPT {sptLine}; it probably targets a newer SPT.",
                release,
                version
            );
        }

        return new GitHubMatch(
            link.Repo,
            link.Origin,
            GitHubMatchStatus.UpdateAvailable,
            $"Newer release on GitHub ({installedVersion} → {version}). GitHub doesn't list SPT versions: check it supports SPT {sptVersion}.",
            release,
            version
        );
    }

    /// <summary>Whether the release's tag, title or notes name the SPT line ("3.10" but not "3.100").</summary>
    internal static bool MentionsSptLine(GitHubRelease release, string sptLine)
    {
        var text = string.Join('\n', release.Tag, release.Name, release.Body);
        var index = 0;
        while ((index = text.IndexOf(sptLine, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 ? ' ' : text[index - 1];
            var afterIndex = index + sptLine.Length;
            var after = afterIndex >= text.Length ? ' ' : text[afterIndex];
            if (!char.IsDigit(before) && before != '.' && !char.IsDigit(after))
            {
                return true;
            }

            index = afterIndex;
        }

        return false;
    }
}
