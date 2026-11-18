namespace ModPulse.Utils;

/// <summary>
/// Provides centralized name normalization for mod matching operations.
/// </summary>
public static class ModNameNormalizer
{
    private static readonly char[] _charsToRemove = ['-', '_', ' ', '.'];
    private static readonly string[] _suffixesToRemove = ["server", "client"];

    /// <summary>
    /// Normalizes a mod name for comparison by removing special characters,
    /// converting to lowercase, and optionally removing server/client suffixes.
    /// </summary>
    /// <param name="name">The name to normalize.</param>
    /// <param name="removeComponentSuffixes">Whether to remove "server" and "client" suffixes.</param>
    public static string Normalize(string? name, bool removeComponentSuffixes = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var result = _charsToRemove.Aggregate(
            name.ToLowerInvariant(),
            (current, c) => current.Replace(c.ToString(), string.Empty)
        );

        if (removeComponentSuffixes)
        {
            var matchingSuffix = _suffixesToRemove.FirstOrDefault(s => result.EndsWith(s, StringComparison.Ordinal));
            if (matchingSuffix is not null)
            {
                result = result[..^matchingSuffix.Length];
            }
        }

        return result;
    }

    /// <summary>
    /// Trailing GUID segments that name a part of a mod rather than the mod: "com.chazut.orbit.server" is ORBIT's
    /// server part.
    /// </summary>
    internal static readonly HashSet<string> ComponentSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "server",
        "client",
        "core",
        "common",
        "shared",
        "plugin",
    };

    /// <summary>
    /// Trailing GUID segments that name another mod this one integrates with: "com.keepmealive.fika" is KeepMeAlive's
    /// Fika build, not Fika.
    /// </summary>
    private static readonly HashSet<string> _integrationSegments = new(StringComparer.OrdinalIgnoreCase) { "fika" };

    /// <summary>
    /// Extracts a readable name from a mod GUID (e.g., "com.author.modname" -> "modname"). When the last dotted segment
    /// only names a part ("server", "core"…) or an integration target ("fika"), the segment before it is the mod's name:
    /// "com.chazut.orbit.server" -> "orbit", "com.keepmealive.fika" -> "keepmealive", "com.fika.core" -> "fika". This
    /// needs at least three dotted segments, so a bare "Fika" stays "Fika".
    /// </summary>
    /// <param name="guid">The GUID to extract from.</param>
    /// <returns>The extracted name, or the original GUID if extraction fails.</returns>
    public static string ExtractNameFromGuid(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
        {
            return string.Empty;
        }

        var dotted = guid.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (
            dotted.Length >= 3
            && (ComponentSegments.Contains(dotted[^1]) || _integrationSegments.Contains(dotted[^1]))
        )
        {
            guid = string.Join('.', dotted[..^1]);
        }

        var parts = guid.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return guid;
        }

        return parts[^1];
    }

    /// <summary>
    /// Whether two GUIDs are parts of one mod by the usual naming convention: identical once a trailing part or
    /// integration segment is dropped ("com.chazut.orbit.server" and "com.chazut.orbit", or
    /// "com.manimal.icebreaker" and "com.manimal.icebreaker.fika"). The shared base needs at least two segments, so
    /// "author.server" and "author" don't count.
    /// </summary>
    public static bool AreComponentGuids(string? guid1, string? guid2)
    {
        if (string.IsNullOrWhiteSpace(guid1) || string.IsNullOrWhiteSpace(guid2))
        {
            return false;
        }

        var base1 = StripComponentSegment(guid1);
        var base2 = StripComponentSegment(guid2);

        return base1.Count(c => c == '.') >= 1
            && string.Equals(base1, base2, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(guid1, guid2, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripComponentSegment(string guid)
    {
        var lastDot = guid.LastIndexOf('.');
        if (lastDot <= 0)
        {
            return guid;
        }

        var last = guid[(lastDot + 1)..];
        return ComponentSegments.Contains(last) || _integrationSegments.Contains(last) ? guid[..lastDot] : guid;
    }

    /// <summary>
    /// Calculates the fuzzy match score between two names.
    /// </summary>
    /// <param name="name1">The first name.</param>
    /// <param name="name2">The second name.</param>
    /// <returns>A score from 0-100 indicating similarity.</returns>
    public static int GetFuzzyMatchScore(string? name1, string? name2)
    {
        var normalized1 = Normalize(name1);
        var normalized2 = Normalize(name2);

        if (string.IsNullOrEmpty(normalized1) || string.IsNullOrEmpty(normalized2))
        {
            return 0;
        }

        return FuzzySharp.Fuzz.Ratio(normalized1, normalized2);
    }

    /// <summary>
    /// Determines if two names match exactly after normalization.
    /// </summary>
    /// <param name="name1">The first name.</param>
    /// <param name="name2">The second name.</param>
    /// <param name="removeComponentSuffixes">Whether to remove server/client suffixes.</param>
    public static bool IsExactMatch(string? name1, string? name2, bool removeComponentSuffixes = false)
    {
        var normalized1 = Normalize(name1, removeComponentSuffixes);
        var normalized2 = Normalize(name2, removeComponentSuffixes);

        return !string.IsNullOrEmpty(normalized1) && string.Equals(normalized1, normalized2, StringComparison.Ordinal);
    }
}
