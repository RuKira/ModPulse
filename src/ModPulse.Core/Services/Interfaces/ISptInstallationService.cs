using ModPulse.Models;

namespace ModPulse.Services.Interfaces;

/// <summary>
/// Service responsible for SPT installation validation.
/// </summary>
public interface ISptInstallationService
{
    /// <summary>
    /// Reads and validates the SPT version from the installation.
    /// </summary>
    /// <param name="sptPath">Path to the SPT installation directory.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The installed version and the version to use for Forge queries (a same-minor stand-in when the Forge doesn't list
    /// the installed patch), or null if validation fails.
    /// </returns>
    Task<SptVersionCheck?> GetAndValidateSptVersionAsync(
        string sptPath,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks for available SPT updates and displays them to the user.
    /// </summary>
    /// <param name="currentVersion">The currently installed SPT version.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of available SPT version updates, or empty list if none available or an error occurred.</returns>
    Task<List<SptVersionResult>> CheckForSptUpdatesAsync(
        SemanticVersioning.Version currentVersion,
        CancellationToken cancellationToken = default
    );
}
