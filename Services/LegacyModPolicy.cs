using ModPulse.Models;
using ModPulse.Utils;

namespace ModPulse.Services;

/// <summary>
/// Rules for legacy SPT installs (below 3.11, i.e. 3.9.x and 3.10.x):
/// <list type="bullet">
/// <item>every mod is labeled legacy;</item>
/// <item>the installed mod version is assumed to match the installed SPT version (never flagged incompatible);</item>
/// <item>an update is only offered when Forge confirms the target version supports the installed SPT version, so a
/// legacy mod is never pointed at a build made for 3.11+.</item>
/// </list>
/// </summary>
public static class LegacyModPolicy
{
    /// <summary>Note shown when an update was withheld because it targets a newer SPT.</summary>
    public const string WithheldUpdateNote = "Newer versions target a newer SPT; keeping the installed legacy version.";

    /// <summary>
    /// Applies the legacy rules to every mod. Returns how many available updates were withheld.
    /// </summary>
    public static int ApplyAll(IEnumerable<Mod> mods, SemanticVersioning.Version sptVersion)
    {
        var withheld = 0;
        foreach (var mod in mods)
        {
            if (Apply(mod, sptVersion))
            {
                withheld++;
            }
        }

        return withheld;
    }

    /// <summary>
    /// Applies the legacy rules to one mod (after Forge update data is attached). Returns true when an available
    /// update was withheld.
    /// </summary>
    public static bool Apply(Mod mod, SemanticVersioning.Version sptVersion)
    {
        mod.MarkLegacy();

        switch (mod.UpdateStatus)
        {
            case UpdateStatus.UpdateAvailable when !TargetSupportsSpt(mod, mod.LatestVersion, sptVersion):
                mod.KeepInstalledVersion(WithheldUpdateNote);
                return true;

            case UpdateStatus.Incompatible:
                // Forge has nothing for this SPT version; on legacy the installed version is trusted.
                mod.KeepInstalledVersion(null);
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// True when Forge confirms <paramref name="targetVersion"/> supports <paramref name="sptVersion"/>, through the
    /// update's SPT version list or that version's SPT constraint. Unknown means no.
    /// </summary>
    public static bool TargetSupportsSpt(Mod mod, string? targetVersion, SemanticVersioning.Version sptVersion)
    {
        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            return false;
        }

        var installed = new SemanticVersioning.Version(sptVersion.Major, sptVersion.Minor, sptVersion.Patch);

        if (SemVer.AnySupportsSpt(mod.LatestVersionSptVersions, installed))
        {
            return true;
        }

        var constraint = mod.ApiVersions?.FirstOrDefault(v =>
                string.Equals(SptLayout.Normalize(v.Version), SptLayout.Normalize(targetVersion), StringComparison.Ordinal)
            )
            ?.SptVersionConstraint;

        return SemVer.SupportsSpt(constraint, installed);
    }
}
