using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ModPulse.Utils;

/// <summary>
/// Identifies library DLLs in BepInEx/plugins: DLLs with no <c>[BepInPlugin]</c> that other mods depend on (Effects
/// Manager Framework ships as plain <c>EFTEffectManager.dll</c>). Such a DLL is recognized when it carries a
/// reverse-DNS GUID string naming itself: <c>com.eft.effectmanager.core</c> inside <c>EFTEffectManager.dll</c>. The
/// name check keeps out DLLs that merely mention other mods' GUIDs.
/// </summary>
public static partial class LibraryDllIdentity
{
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9_-]+){2,}")]
    private static partial Regex GuidLike();

    /// <summary>The library's GUID and version, or null when it doesn't identify itself.</summary>
    public static (string Guid, string Version)? TryIdentify(string dllPath, string? assemblyName)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(dllPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var name = assemblyName ?? Path.GetFileNameWithoutExtension(dllPath);
        var guid = PickGuid(ExtractCandidateStrings(bytes), name);
        if (guid is null)
        {
            return null;
        }

        return (guid, ReadVersion(dllPath));
    }

    /// <summary>
    /// The candidate GUID that names the library itself: its name part (see
    /// <see cref="ModNameNormalizer.ExtractNameFromGuid"/>, at least 4 characters) appears in the DLL's own name. When
    /// several qualify, the longest name part wins.
    /// </summary>
    internal static string? PickGuid(IEnumerable<string> candidates, string dllName)
    {
        var normalizedDll = ModNameNormalizer.Normalize(dllName);
        if (normalizedDll.Length == 0)
        {
            return null;
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(g => !IsFrameworkName(g))
            .Select(g => (Guid: g, Name: ModNameNormalizer.Normalize(ModNameNormalizer.ExtractNameFromGuid(g))))
            .Where(c => c.Name.Length >= 4 && normalizedDll.Contains(c.Name, StringComparison.Ordinal))
            .OrderByDescending(c => c.Name.Length)
            .Select(c => c.Guid)
            .FirstOrDefault();
    }

    /// <summary>Framework and engine namespaces are never mod GUIDs.</summary>
    private static bool IsFrameworkName(string guid)
    {
        string[] prefixes = ["System.", "Microsoft.", "Unity.", "UnityEngine.", "Mono.", "Newtonsoft."];
        return prefixes.Any(p => guid.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reverse-DNS-looking strings in the DLL (UTF-16 user strings and UTF-8 metadata/attribute blobs).</summary>
    internal static IEnumerable<string> ExtractCandidateStrings(byte[] bytes)
    {
        foreach (var text in new[] { Encoding.Unicode.GetString(bytes), Encoding.UTF8.GetString(bytes) })
        {
            foreach (Match match in GuidLike().Matches(text))
            {
                if (match.Value.Length <= 128)
                {
                    yield return match.Value;
                }
            }
        }
    }

    private static string ReadVersion(string dllPath)
    {
        var info = FileVersionInfo.GetVersionInfo(dllPath);
        var version = info.ProductVersion ?? info.FileVersion ?? "0.0.0";
        var plus = version.IndexOf('+');
        return (plus >= 0 ? version[..plus] : version).Trim();
    }
}
