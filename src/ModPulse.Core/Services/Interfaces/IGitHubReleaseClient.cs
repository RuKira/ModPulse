using ModPulse.Models;

namespace ModPulse.Services.Interfaces;

/// <summary>What a GitHub release lookup found.</summary>
public enum GitHubLookupStatus
{
    /// <summary>A release (or a version tag) was found.</summary>
    Found,

    /// <summary>The repository exists but has no releases and no tags.</summary>
    NoReleases,

    /// <summary>The repository doesn't exist or is private.</summary>
    NotFound,

    /// <summary>GitHub's rate limit is used up.</summary>
    RateLimited,

    /// <summary>The request failed (network, timeout, unexpected response).</summary>
    Failed,
}

/// <summary>The result of <see cref="IGitHubReleaseClient.GetLatestAsync"/>.</summary>
public sealed record GitHubLookup(
    GitHubLookupStatus Status,
    GitHubRelease? Release = null,
    DateTimeOffset? RateLimitResetUtc = null
);

/// <summary>Reads the latest release of a GitHub repository. Never downloads release files.</summary>
public interface IGitHubReleaseClient
{
    /// <summary>
    /// The latest published release (drafts and pre-releases skipped), or the highest version tag when the repository
    /// has no releases.
    /// </summary>
    /// <param name="repo">The repository as <c>owner/repo</c>.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    Task<GitHubLookup> GetLatestAsync(string repo, CancellationToken cancellationToken = default);
}
