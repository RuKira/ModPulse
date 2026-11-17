namespace ModPulse.Utils;

/// <summary>
/// Helpers for parsing semantic version strings.
/// </summary>
public static class SemVer
{
    /// <summary>
    /// Parses a semantic version, returning null when the string is missing or invalid.
    /// </summary>
    /// <param name="version">The version string to parse.</param>
    /// <returns>The parsed version, or null if it could not be parsed.</returns>
    public static SemanticVersioning.Version? TryParse(string? version)
    {
        return !string.IsNullOrWhiteSpace(version) && SemanticVersioning.Version.TryParse(version, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Parses a semantic version, falling back to 0.0.0 when the string is missing or invalid.
    /// </summary>
    /// <param name="version">The version string to parse.</param>
    /// <returns>The parsed version, or 0.0.0 if it could not be parsed.</returns>
    public static SemanticVersioning.Version ParseOrZero(string? version)
    {
        return TryParse(version) ?? new SemanticVersioning.Version(0, 0, 0);
    }

    /// <summary>
    /// Determines whether <paramref name="version"/> satisfies the given SPT version constraint (a semver range).
    /// Returns false when the constraint is missing or cannot be parsed.
    /// </summary>
    /// <param name="constraint">The semver range constraint (e.g. "~4.0.0").</param>
    /// <param name="version">The version to test against the constraint.</param>
    /// <returns>True if the version satisfies the constraint; false if it does not or the constraint is invalid.</returns>
    public static bool SatisfiesRange(string? constraint, SemanticVersioning.Version version)
    {
        return !string.IsNullOrWhiteSpace(constraint)
            && SemanticVersioning.Range.TryParse(constraint, out var range)
            && range.IsSatisfied(version);
    }

    /// <summary>
    /// Whether a mod built for <paramref name="constraint"/> runs on SPT <paramref name="sptVersion"/>. SPT patch
    /// releases stay compatible within a minor line, so a mod built for an older or equal patch of the same
    /// MAJOR.MINOR is supported (4.1.5 runs on 4.1.6, 3.11.1 on 3.11.4), but never across minors (a 4.0 mod is not
    /// supported on 4.1, nor a 3.11 mod on 4.0) and never on an older patch than it was built for.
    /// </summary>
    /// <param name="constraint">The mod version's SPT constraint from Forge (e.g. "4.1.5", "~4.0 &lt;4.1.0").</param>
    /// <param name="sptVersion">The installed SPT version.</param>
    /// <returns>True when supported; false when not or when the constraint is missing or invalid.</returns>
    public static bool SupportsSpt(string? constraint, SemanticVersioning.Version sptVersion)
    {
        if (string.IsNullOrWhiteSpace(constraint) || !SemanticVersioning.Range.TryParse(constraint, out var range))
        {
            return false;
        }

        for (var patch = sptVersion.Patch; patch >= 0; patch--)
        {
            if (range.IsSatisfied(new SemanticVersioning.Version(sptVersion.Major, sptVersion.Minor, patch)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any of Forge's listed SPT versions for a mod version is supported on <paramref name="sptVersion"/>
    /// under the same rule as <see cref="SupportsSpt"/> (same MAJOR.MINOR, patch at or below the installed one).
    /// </summary>
    public static bool AnySupportsSpt(IEnumerable<string>? listedSptVersions, SemanticVersioning.Version sptVersion)
    {
        return listedSptVersions?.Any(listed =>
                TryParse(listed) is { } v
                && v.Major == sptVersion.Major
                && v.Minor == sptVersion.Minor
                && v.Patch <= sptVersion.Patch
            ) == true;
    }
}
