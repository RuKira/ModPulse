using ModPulse.Models;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;

namespace ModPulse.Services;

/// <summary>
/// Service responsible for SPT installation validation.
/// </summary>
[Injectable(InjectionType.Transient)]
public sealed class SptInstallationService(
    IForgeApiService forgeApiService,
    IModScannerService scannerService,
    IModCheckReporter reporter,
    ILogger<SptInstallationService> logger
) : ISptInstallationService
{
    /// <inheritdoc />
    public async Task<SptVersionCheck?> GetAndValidateSptVersionAsync(
        string sptPath,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogDebug("Validating SPT installation at: {SptPath}", sptPath);

        var installation = SptLayout.Resolve(sptPath);
        if (installation is null)
        {
            logger.LogError("No SPT server found in or below {SptPath}", sptPath);
            reporter.Error(
                $"Error: Could not find an SPT installation ({SptLayout.SupportedRangeText}). Run this file in your root SPT directory, or provide the SPT path as an argument."
            );
            return null;
        }

        logger.LogDebug(
            "SPT server kind {Kind} at {ServerRoot} (game root {GameRoot})",
            installation.Kind,
            installation.ServerRoot,
            installation.GameRoot
        );

        var localSptVersionStr = scannerService.GetSptVersion(sptPath);

        if (string.IsNullOrWhiteSpace(localSptVersionStr))
        {
            var source = installation.Kind == SptServerKind.DotNet
                ? "SPTarkov.Server.Core.dll"
                : "SPT_Data/Server/configs/core.json or SPT.Server.exe";
            logger.LogError("Could not read the SPT version from {Source}", source);
            reporter.Error($"Error: SPT version not found in {source}.");
            return null;
        }

        logger.LogDebug("Found local SPT version: {SptVersion}", localSptVersionStr);

        reporter.ValidatingSptVersion(localSptVersionStr);

        var validationResult = await forgeApiService.ValidateSptVersionAsync(localSptVersionStr, cancellationToken);

        var localSptVersion = SemVer.TryParse(localSptVersionStr);
        if (localSptVersion is null)
        {
            logger.LogError("Could not parse SPT version '{SptVersion}'", localSptVersionStr);
            reporter.Error($"Failed. Could not parse SPT version '{localSptVersionStr}'.");
            return null;
        }

        var forgeVersion = localSptVersion;

        if (validationResult.TryPickT2(out var apiError, out _))
        {
            reporter.Error($"Failed. API error: {apiError.Message}");
            return null;
        }

        if (!validationResult.TryPickT0(out var isValid, out _) || !isValid)
        {
            // The Forge doesn't list every patch (3.11.5 isn't there; its newest 3.11 is 3.11.4). Use the newest patch it
            // lists in the same minor at or below the installed one; mods for that patch work under the same-minor rule.
            var standIn = await FindForgeStandInAsync(localSptVersion, cancellationToken);
            if (standIn is null)
            {
                reporter.Error("Failed. SPT version not recognized by Forge API.");
                return null;
            }

            forgeVersion = standIn;
            logger.LogWarning(
                "Forge doesn't list SPT {Installed}; using {StandIn} for Forge lookups",
                localSptVersion,
                standIn
            );
        }

        reporter.Success("OK");

        if (forgeVersion != localSptVersion)
        {
            reporter.Warning(
                $"The Forge doesn't list SPT {localSptVersion}, so mods are checked against SPT {forgeVersion}, the newest {forgeVersion.Major}.{forgeVersion.Minor} release it knows. Mods for {forgeVersion} work on {localSptVersion}."
            );
        }

        if (!SptLayout.IsSupported(localSptVersion))
        {
            logger.LogWarning("SPT {Version} is outside the supported range", localSptVersion);
            reporter.Warning(
                $"SPT {localSptVersion} is outside the versions ModPulse supports ({SptLayout.SupportedRangeText}). Results may be incomplete."
            );
        }

        var folderMismatch = SptLayout.DescribeFolderMismatch(installation, localSptVersion);
        if (folderMismatch is not null)
        {
            logger.LogWarning("Server folder/version mismatch: {Message}", folderMismatch);
            reporter.Warning(folderMismatch);
        }

        if (SptLayout.IsLegacy(localSptVersion))
        {
            reporter.Warning(
                $"SPT {localSptVersion} is a legacy version (below 3.11). Limited support: installed mods are assumed to match this SPT version, mods are labeled Legacy, and only updates published for SPT {localSptVersion.Major}.{localSptVersion.Minor} (up to {localSptVersion}) are offered."
            );
        }

        return new SptVersionCheck(localSptVersion, forgeVersion);
    }

    /// <summary>
    /// The newest Forge-listed SPT version with the same MAJOR.MINOR as <paramref name="installed"/> and a patch at or
    /// below it, or null when the Forge lists none (or can't be reached).
    /// </summary>
    private async Task<SemanticVersioning.Version?> FindForgeStandInAsync(
        SemanticVersioning.Version installed,
        CancellationToken cancellationToken
    )
    {
        var versions = await forgeApiService.GetSptVersionsInMinorAsync(installed.Major, installed.Minor, cancellationToken);
        return versions.Match(
            list => PickStandIn(installed, list.Select(v => v.Version)),
            _ => null
        );
    }

    /// <summary>Same-minor stand-in selection (see <see cref="FindForgeStandInAsync"/>).</summary>
    internal static SemanticVersioning.Version? PickStandIn(
        SemanticVersioning.Version installed,
        IEnumerable<string> forgeVersions
    )
    {
        return forgeVersions
            .Select(SemVer.TryParse)
            .Where(v =>
                v is not null && v.Major == installed.Major && v.Minor == installed.Minor && v.Patch <= installed.Patch
            )
            .OrderByDescending(v => v)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<List<SptVersionResult>> CheckForSptUpdatesAsync(
        SemanticVersioning.Version currentVersion,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogDebug("Checking for SPT updates. Current version: {CurrentVersion}", currentVersion);

        var versionsResult = await forgeApiService.GetAllSptVersionsAsync(cancellationToken);

        return versionsResult.Match(
            versions =>
            {
                // Filter to versions newer than the current version, skipping any that can't be parsed.
                var newerVersions = versions
                    .Select(v => (Raw: v, Parsed: SemVer.TryParse(v.Version)))
                    .Where(x => x.Parsed is not null && x.Parsed! > currentVersion)
                    .OrderByDescending(x => x.Parsed)
                    .Select(x => x.Raw)
                    .ToList();

                logger.LogDebug("Found {Count} newer SPT versions", newerVersions.Count);
                return newerVersions;
            },
            apiError =>
            {
                logger.LogWarning("Failed to check for SPT updates: {Error}", apiError.Message);
                return [];
            }
        );
    }
}
