using System.Text.RegularExpressions;

namespace ModPulse.Utils;

/// <summary>
/// Reads versions from GitHub tags and installed mods for comparison. Tags come in many shapes (<c>v1.2.3</c>,
/// <c>1.2</c>, <c>MyMod-1.2.3</c>, <c>MyMod_v1.2.3</c>); a tag with no version at its end isn't guessed at.
/// </summary>
public static partial class ReleaseVersion
{
    // The version at the end of a tag: optional v, 1-4 numeric parts, optional pre-release, optionally preceded by a
    // name and a separator.
    [GeneratedRegex(@"(?:^|[-_ /])[vV]?(?<v>\d+(?:\.\d+){0,3})(?<pre>-[0-9A-Za-z.-]+)?$")]
    private static partial Regex TagVersion();

    /// <summary>The version a tag names, or null when it doesn't end in one.</summary>
    public static SemanticVersioning.Version? FromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var match = TagVersion().Match(tag.Trim());
        return match.Success ? Build(match.Groups["v"].Value, match.Groups["pre"].Value) : null;
    }

    /// <summary>
    /// An installed mod's version, read the same way as tags so <c>1.2.3.0</c> compares equal to <c>v1.2.3</c>.
    /// </summary>
    public static SemanticVersioning.Version? FromInstalled(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var text = version.Trim();
        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        return FromTag(text);
    }

    /// <summary>
    /// Builds a semantic version from 1-4 numeric parts; missing parts are 0. A fourth part (an assembly revision) has
    /// no SemVer slot and is dropped, so <c>1.2.3.0</c> equals <c>v1.2.3</c>.
    /// </summary>
    private static SemanticVersioning.Version? Build(string numbers, string preRelease)
    {
        var parts = numbers.Split('.').Select(p => int.TryParse(p, out var n) ? n : -1).ToList();
        if (parts.Count == 0 || parts.Any(p => p < 0))
        {
            return null;
        }

        while (parts.Count < 3)
        {
            parts.Add(0);
        }

        var text = $"{parts[0]}.{parts[1]}.{parts[2]}";

        if (preRelease.Length > 0)
        {
            text += preRelease;
        }

        return SemVer.TryParse(text);
    }
}
