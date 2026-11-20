using ModPulse.Models;

namespace ModPulse.Services;

/// <summary>
/// Checks the dependencies plugins declare in their own DLLs (<c>[BepInDependency]</c>). Forge only knows the
/// dependencies an author registered there; GamePanelHUD, for example, requires EFTApi/KmyTarkovApi in its DLL but lists
/// none on the Forge. A hard dependency whose GUID isn't installed means BepInEx won't load the plugin.
/// </summary>
public static class LocalDependencyChecker
{
    /// <summary>
    /// Hard dependencies declared by installed mods that no installed mod provides, with the mods that need each one.
    /// </summary>
    /// <param name="mods">The installed mods (their GUIDs and alternate GUIDs count as installed).</param>
    /// <param name="builtInGuids">GUIDs of plugins SPT itself ships (BepInEx/plugins/spt), always present.</param>
    public static List<(string Guid, List<Mod> RequiredBy)> FindMissing(
        IReadOnlyList<Mod> mods,
        IEnumerable<string> builtInGuids
    )
    {
        var installed = mods.SelectMany(m => new[] { m.Guid }.Concat(m.AlternateGuids))
            .Concat(builtInGuids)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new Dictionary<string, List<Mod>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            foreach (var dependency in mod.DeclaredDependencies.Where(d => d.IsHard && !installed.Contains(d.Guid)))
            {
                if (!missing.TryGetValue(dependency.Guid, out var requiredBy))
                {
                    requiredBy = [];
                    missing[dependency.Guid] = requiredBy;
                }

                if (!requiredBy.Contains(mod))
                {
                    requiredBy.Add(mod);
                }
            }
        }

        return missing.Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
