using ModPulse.Models;

namespace ModPulse.Utils;

/// <summary>
/// Guards fuzzy name matches against picking a different author's mod that merely has a similar name (e.g. a local
/// "Hardcore Ruleset" by RuKira vs. "Hardcore Rules" by DanW on the Forge).
/// </summary>
public static class ForgeMatchGuard
{
    /// <summary>
    /// True when the Forge result clearly belongs to someone else: both sides carry GUIDs, none of the local GUIDs
    /// equal the Forge GUID, and neither the author names nor the GUID author namespaces line up.
    /// </summary>
    public static bool IsDifferentMod(Mod mod, ModSearchResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Guid))
        {
            return false; // Nothing to compare against; keep Refringe's original behavior.
        }

        var localGuids = new[] { mod.Guid }.Concat(mod.AlternateGuids).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        if (localGuids.Count == 0)
        {
            return false;
        }

        if (localGuids.Any(g => string.Equals(g, result.Guid, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !SameAuthor(mod, localGuids, result);
    }

    /// <summary>
    /// True when the local GUID is the Forge listing's GUID plus one core-component segment, i.e. the local DLL is
    /// the server/client half of that mod: <c>com.chazut.orbit</c> on the Forge vs. <c>com.chazut.orbit.server</c>
    /// locally. Only core components qualify because their version tracks the mod's; integration plugins such as
    /// <c>com.chazut.orbit.fika</c> carry their own version numbers and would be compared against the wrong releases.
    /// The Forge GUID must be fully qualified (three or more segments) so short GUIDs like "fika" can't claim every
    /// "fika.*" plugin.
    /// </summary>
    public static bool IsComponentOf(Mod mod, ModSearchResult result)
    {
        var forgeGuid = result.Guid;
        if (string.IsNullOrWhiteSpace(forgeGuid) || forgeGuid.Split('.', StringSplitOptions.RemoveEmptyEntries).Length < 3)
        {
            return false;
        }

        var prefix = forgeGuid + ".";
        return new[] { mod.Guid }
            .Concat(mod.AlternateGuids)
            .Any(g =>
                !string.IsNullOrWhiteSpace(g)
                && g.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && _coreComponentSegments.Contains(g[prefix.Length..])
            );
    }

    /// <summary>GUID suffixes that name a mod's own server/client halves (not separately versioned add-ons).</summary>
    private static readonly HashSet<string> _coreComponentSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "server",
        "client",
        "core",
        "common",
        "shared",
        "plugin",
    };

    private static bool SameAuthor(Mod mod, List<string> localGuids, ModSearchResult result)
    {
        var localAuthor = Simplify(mod.LocalAuthor);
        var forgeOwner = Simplify(result.Owner?.Name);
        var forgeNamespace = AuthorNamespace(result.Guid);

        if (localAuthor.Length > 0 && (localAuthor == forgeOwner || localAuthor == forgeNamespace))
        {
            return true;
        }

        return localGuids.Select(AuthorNamespace).Any(ns => ns.Length > 0 && (ns == forgeNamespace || ns == forgeOwner));
    }

    /// <summary>
    /// The author part of a reverse-domain GUID: "rukira" in "com.rukira.hardcoreruleset". Empty when the GUID
    /// isn't in that form.
    /// </summary>
    internal static string AuthorNamespace(string? guid)
    {
        var parts = (guid ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? Simplify(parts[1]) : string.Empty;
    }

    private static string Simplify(string? value)
    {
        return new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
