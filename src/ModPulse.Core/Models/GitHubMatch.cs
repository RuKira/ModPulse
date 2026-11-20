namespace ModPulse.Models;

/// <summary>What a linked GitHub repository says about an off-Forge mod.</summary>
public enum GitHubMatchStatus
{
    /// <summary>The latest release is newer than the installed version.</summary>
    UpdateAvailable,

    /// <summary>The installed version is the latest release.</summary>
    UpToDate,

    /// <summary>The installed version is newer than the latest release.</summary>
    NewerInstalled,

    /// <summary>
    /// Legacy SPT only: a newer release exists, but nothing in it mentions the installed SPT version, so it probably
    /// targets a newer SPT. Shown as information, not as an update.
    /// </summary>
    HeldBack,

    /// <summary>The repository couldn't be checked, or its release has no readable version.</summary>
    Unknown,
}

/// <summary>A release (or tag) found on GitHub.</summary>
/// <param name="Tag">The tag name, e.g. <c>v1.2.3</c>.</param>
/// <param name="Name">The release title, if any.</param>
/// <param name="Body">The release notes, if any.</param>
/// <param name="Url">The release page (or the tag's page when there are no releases).</param>
/// <param name="PublishedUtc">When it was published, if known.</param>
/// <param name="Assets">Names of the release's files.</param>
/// <param name="FromTags">True when no release exists and the newest version tag was used instead.</param>
public sealed record GitHubRelease(
    string Tag,
    string? Name,
    string? Body,
    string Url,
    DateTimeOffset? PublishedUtc,
    IReadOnlyList<string> Assets,
    bool FromTags = false
);

/// <summary>The result of checking an off-Forge mod against its linked GitHub repository.</summary>
/// <param name="Repo">The repository as <c>owner/repo</c>.</param>
/// <param name="Origin">Whether the user or the built-in community list linked it.</param>
/// <param name="Status">What the latest release means for the installed version.</param>
/// <param name="Reason">One sentence explaining the status.</param>
/// <param name="Release">The latest release, when one was found.</param>
/// <param name="Version">The version read from the release's tag, when readable.</param>
public sealed record GitHubMatch(
    string Repo,
    GitHubSourceOrigin Origin,
    GitHubMatchStatus Status,
    string Reason,
    GitHubRelease? Release = null,
    string? Version = null
)
{
    /// <summary>The repository's page.</summary>
    public string RepoUrl
    {
        get { return "https://github.com/" + Repo; }
    }

    /// <summary>Where to get the release: its page, or the repository's releases page.</summary>
    public string ReleaseUrl
    {
        get { return Release?.Url ?? RepoUrl + "/releases"; }
    }
}
