namespace ModPulse.Models;

/// <summary>
/// The outcome of a ModPulse self-update check.
/// </summary>
public enum ModPulseUpdateStatus
{
    /// <summary>The running version is the newest release (or newer, e.g. a dev build).</summary>
    UpToDate,

    /// <summary>A newer release is published on GitHub.</summary>
    UpdateAvailable,

    /// <summary>The check couldn't be completed (no release published yet, or a network/API error).</summary>
    Unavailable,
}

/// <summary>
/// Describes the result of a ModPulse self-update check.
/// </summary>
/// <param name="Status">The update status.</param>
/// <param name="CurrentVersion">The running application version.</param>
/// <param name="LatestVersion">The latest released version, when known.</param>
/// <param name="DownloadLink">The release page for the latest version, when known.</param>
public sealed record ModPulseUpdateResult(
    ModPulseUpdateStatus Status,
    string CurrentVersion,
    string? LatestVersion = null,
    string? DownloadLink = null
);
