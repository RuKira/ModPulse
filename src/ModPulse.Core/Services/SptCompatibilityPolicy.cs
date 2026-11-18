using ModPulse.Models;
using ModPulse.Utils;

namespace ModPulse.Services;

/// <summary>
/// Forge's mods/updates endpoint matches the SPT version exactly, so a mod whose newest build targets 4.1.5 comes back
/// as "incompatible" (<c>no_version_for_spt</c>) on a 4.1.6 server. ModPulse's rule is that a build for an older or
/// equal patch of the same SPT minor is supported (<see cref="SemVer.SupportsSpt"/>), so those answers are
/// re-evaluated against the mod's version list here.
/// </summary>
public static class SptCompatibilityPolicy
{
    /// <summary>
    /// Re-evaluates every mod Forge reported as incompatible. Returns how many were resolved (to up to date or to an
    /// update).
    /// </summary>
    public static int ResolveAll(IEnumerable<Mod> mods, SemanticVersioning.Version sptVersion)
    {
        return mods.Count(mod => Resolve(mod, sptVersion));
    }

    /// <summary>
    /// Re-evaluates one mod. Returns true when Forge's "incompatible" was overridden because some version supports the
    /// installed SPT within its minor line.
    /// </summary>
    public static bool Resolve(Mod mod, SemanticVersioning.Version sptVersion)
    {
        if (mod.UpdateStatus != UpdateStatus.Incompatible || mod.ApiVersions is not { Count: > 0 } versions)
        {
            return false;
        }

        var best = versions
            .Where(v => SemVer.SupportsSpt(v.SptVersionConstraint, sptVersion))
            .OrderByDescending(v => SemVer.ParseOrZero(v.Version))
            .FirstOrDefault();

        if (best is null)
        {
            return false;
        }

        var isNewer = SemVer.ParseOrZero(best.Version) > SemVer.ParseOrZero(mod.LocalVersion);
        var link =
            mod.ApiModId is { } id ? ForgeUrls.Download(id, mod.ApiSlug, best.Version) : best.Link;

        mod.ResolveIncompatibleWithinMinor(best.Version, link, isNewer);
        return true;
    }
}
