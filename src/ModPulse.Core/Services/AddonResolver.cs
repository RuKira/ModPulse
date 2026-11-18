using ModPulse.Models;
using ModPulse.Utils;

namespace ModPulse.Services;

/// <summary>
/// Identifies installed plugins that the Forge doesn't list as mods but that belong to an installed mod:
/// <list type="bullet">
/// <item>
/// <b>Addons</b>: separate Forge downloads attached to a parent mod (ORBIT - Fika Addon, Door Randomizer - Fika Sync).
/// Addons have no GUID on the Forge, so the parent is found locally first, then its addon list is searched by name.
/// </item>
/// <item>
/// <b>Bundled parts</b>: extra plugins shipped inside the parent's own download (Show Me The Money's Quick Sell). These
/// extend the parent's GUID but match none of its addons.
/// </item>
/// </list>
/// A parent is an installed, Forge-matched mod whose GUID is a prefix of the plugin's GUID ("com.chazut.orbit" for
/// "com.chazut.orbit.fika"), one installed in the same plugins/mods folder (Black Div Notifier inside BlackDiv/), or,
/// as a weaker link, one the plugin is named after ("DoorRandomizer.Sync" for Door Randomizer). Only a GUID link can make
/// a plugin a bundled part; the other links need a matching addon on the Forge.
/// </summary>
public static class AddonResolver
{
    /// <summary>Best addon-name score needed when the parent has several addons.</summary>
    internal const int NameThreshold = 70;

    /// <summary>Lower bar when the parent has exactly one addon (the folder or GUID link already points at it).</summary>
    internal const int SingleAddonThreshold = 50;

    /// <summary>
    /// Resolves every unmatched mod. <paramref name="getAddons"/> lists a parent mod's addons by Forge ID (empty on
    /// error). Returns how many mods were identified as addons or bundled parts.
    /// </summary>
    public static async Task<int> ResolveAsync(
        IReadOnlyList<Mod> mods,
        Func<int, Task<IReadOnlyList<AddonResult>>> getAddons
    )
    {
        var matched = mods.Where(m => m.IsMatched).ToList();
        var addonCache = new Dictionary<int, IReadOnlyList<AddonResult>>();
        var resolved = 0;

        foreach (var mod in mods.Where(m => !m.IsMatched && !m.BelongsToAnotherMod))
        {
            var parents = FindParents(mod, matched);
            if (parents.Count == 0)
            {
                continue;
            }

            AddonMatch? addonMatch = null;
            foreach (var (parent, _) in parents)
            {
                var parentId = parent.ApiModId!.Value;
                if (!addonCache.TryGetValue(parentId, out var addons))
                {
                    addons = await getAddons(parentId);
                    addonCache[parentId] = addons;
                }

                var addon = PickAddon(mod, addons);
                if (addon is not null)
                {
                    addonMatch = Evaluate(mod, addon, parent);
                    break;
                }
            }

            if (addonMatch is not null)
            {
                mod.SetAddon(addonMatch);
                resolved++;
                continue;
            }

            // Not an addon: a plugin extending the parent's GUID ships inside the parent mod.
            var guidParent = parents.FirstOrDefault(p => p.ByGuid).Parent;
            if (guidParent is not null)
            {
                mod.SetPartOf(guidParent);
                resolved++;
            }
        }

        return resolved;
    }

    /// <summary>
    /// Installed, Forge-matched mods this plugin may belong to: GUID-prefix parents first (longest prefix first), then
    /// mods sharing its folder.
    /// </summary>
    internal static List<(Mod Parent, bool ByGuid)> FindParents(Mod mod, IReadOnlyList<Mod> matched)
    {
        var localGuids = new[] { mod.Guid }.Concat(mod.AlternateGuids).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        var byGuid = new List<(Mod Parent, int PrefixLength)>();
        var byFolder = new List<Mod>();
        var byName = new List<Mod>();
        var localName = ModNameNormalizer.Normalize(mod.LocalName);
        var localSegments = localGuids
            .SelectMany(g => g.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
            .Select(segment => ModNameNormalizer.Normalize(segment))
            .ToHashSet();
        var modDirectory = Path.GetDirectoryName(mod.FilePath);

        foreach (var candidate in matched)
        {
            if (ReferenceEquals(candidate, mod))
            {
                continue;
            }

            var parentGuids = new[] { candidate.Guid, candidate.ApiGuid }
                .Concat(candidate.AlternateGuids)
                .Where(g => !string.IsNullOrWhiteSpace(g) && g!.Contains('.'))
                .Select(g => g!)
                .ToList();

            var prefix = parentGuids
                .Where(pg => localGuids.Any(lg => lg.StartsWith(pg + ".", StringComparison.OrdinalIgnoreCase)))
                .Select(pg => pg.Length)
                .DefaultIfEmpty(0)
                .Max();

            if (prefix > 0)
            {
                byGuid.Add((candidate, prefix));
            }
            else if (IsModSubfolder(modDirectory) && SharesFolder(modDirectory!, candidate))
            {
                byFolder.Add(candidate);
            }
            else if (NamedAfter(localName, localSegments, candidate))
            {
                byName.Add(candidate);
            }
        }

        return byGuid
            .OrderByDescending(p => p.PrefixLength)
            .Select(p => (p.Parent, true))
            .Concat(byFolder.Select(p => (p, false)))
            .Concat(byName.Select(p => (p, false)))
            .DistinctBy(p => p.Item1.ApiModId)
            .ToList();
    }

    /// <summary>The parent's addon this plugin most likely is, by name; null when none is close enough.</summary>
    internal static AddonResult? PickAddon(Mod mod, IReadOnlyList<AddonResult> addons)
    {
        if (addons.Count == 0)
        {
            return null;
        }

        var scored = addons
            .Select(a => (Addon: a, Score: Math.Max(NameScore(mod.LocalName, a.Name), NameScore(mod.LocalName, a.Slug))))
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scored[0];
        if (addons.Count == 1)
        {
            return best.Score >= SingleAddonThreshold ? best.Addon : null;
        }

        var runnerUp = scored[1].Score;
        return best.Score >= NameThreshold && best.Score - runnerUp >= 10 ? best.Addon : null;
    }

    /// <summary>Compares the installed addon's version with the Forge versions that fit the installed parent.</summary>
    internal static AddonMatch Evaluate(Mod mod, AddonResult addon, Mod parent)
    {
        var parentVersion = SemVer.TryParse(parent.LocalVersion);
        var installed = SemVer.TryParse(mod.LocalVersion);
        var versions = (addon.Versions ?? [])
            .Where(v => SemVer.TryParse(v.Version) is not null)
            .OrderByDescending(v => SemVer.ParseOrZero(v.Version))
            .ToList();

        // A version fits unless its constraint parses and the installed parent fails it (malformed = unknown = fits).
        bool Fits(AddonVersion v)
        {
            return parentVersion is null
                || string.IsNullOrWhiteSpace(v.ModVersionConstraint)
                || !SemanticVersioning.Range.TryParse(v.ModVersionConstraint, out var range)
                || range.IsSatisfied(parentVersion);
        }

        var parentName = parent.DisplayName;
        var bestFit = versions.FirstOrDefault(Fits);
        var newest = versions.FirstOrDefault();
        var installedEntry = installed is null
            ? null
            : versions.FirstOrDefault(v => SemVer.ParseOrZero(v.Version) == installed);

        if (installedEntry is not null && !Fits(installedEntry))
        {
            var hint = bestFit is null
                ? $"No version fits {parentName} {parent.LocalVersion} yet."
                : $"Version {bestFit.Version} fits.";
            return new AddonMatch(
                addon,
                parent,
                AddonStatus.IncompatibleWithParent,
                bestFit?.Version,
                bestFit?.Link,
                $"Addon {mod.LocalVersion} needs {parentName} {installedEntry.ModVersionConstraint}; you have {parent.LocalVersion}. {hint}"
            );
        }

        if (bestFit is not null && installed is not null && SemVer.ParseOrZero(bestFit.Version) > installed)
        {
            return new AddonMatch(
                addon,
                parent,
                AddonStatus.UpdateAvailable,
                bestFit.Version,
                bestFit.Link,
                $"{mod.LocalVersion} → {bestFit.Version} (fits your {parentName} {parent.LocalVersion})"
            );
        }

        if (newest is not null && installed is not null && installed > SemVer.ParseOrZero(newest.Version))
        {
            return new AddonMatch(
                addon,
                parent,
                AddonStatus.NewerInstalled,
                newest.Version,
                null,
                $"Installed {mod.LocalVersion} is newer than the Forge's {newest.Version}."
            );
        }

        return new AddonMatch(
            addon,
            parent,
            AddonStatus.UpToDate,
            bestFit?.Version ?? newest?.Version,
            null,
            $"Latest version for your {parentName} {parent.LocalVersion}."
        );
    }

    private static int NameScore(string? local, string? forge)
    {
        var a = ModNameNormalizer.Normalize(local);
        var b = ModNameNormalizer.Normalize(forge);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        return Math.Max(FuzzySharp.Fuzz.Ratio(a, b), FuzzySharp.Fuzz.PartialRatio(a, b));
    }

    /// <summary>
    /// A folder that belongs to one mod (BepInEx/plugins/X or user/mods/X), not the shared plugins/mods root where
    /// loose DLLs of unrelated mods sit side by side.
    /// </summary>
    private static bool IsModSubfolder(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return !string.Equals(name, "plugins", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "mods", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Weak link: the plugin is named after the parent ("DoorRandomizer.Sync" for Door Randomizer) or its GUID contains
    /// the parent's GUID name ("com.ozen.doorrandomizer.sync" for "xyz.drakia.doorrandomizer"). Short names are ignored.
    /// </summary>
    private static bool NamedAfter(string localName, HashSet<string> localSegments, Mod candidate)
    {
        const int minLength = 5;

        var parentNames = new[] { candidate.DisplayName, candidate.LocalName }
            .Select(n => ModNameNormalizer.Normalize(n, removeComponentSuffixes: true))
            .Where(n => n.Length >= minLength);
        if (parentNames.Any(n => localName.StartsWith(n, StringComparison.Ordinal) && localName.Length > n.Length))
        {
            return true;
        }

        var parentGuidName = ModNameNormalizer.Normalize(ModNameNormalizer.ExtractNameFromGuid(candidate.Guid));
        return parentGuidName.Length >= minLength && localSegments.Contains(parentGuidName);
    }

    private static bool SharesFolder(string directory, Mod candidate)
    {
        return new[] { candidate.FilePath, candidate.PairedComponentPath }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Any(p => string.Equals(Path.GetDirectoryName(p), directory, StringComparison.OrdinalIgnoreCase));
    }
}
