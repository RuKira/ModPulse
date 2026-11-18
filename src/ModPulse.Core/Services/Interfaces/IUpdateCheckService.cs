using ModPulse.Models;

namespace ModPulse.Services.Interfaces;

/// <summary>
/// Checks whether a newer version of ModPulse is published on GitHub Releases.
/// </summary>
public interface IUpdateCheckService
{
    /// <summary>
    /// Checks GitHub Releases for a newer version of ModPulse, relative to the running build.
    /// </summary>
    /// <param name="sptVersion">The installed SPT version (unused by the GitHub check; kept for the call site).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The update status; API errors resolve to <see cref="ModPulseUpdateStatus.Unavailable"/>.</returns>
    Task<ModPulseUpdateResult> CheckAsync(
        SemanticVersioning.Version sptVersion,
        CancellationToken cancellationToken = default
    );
}
