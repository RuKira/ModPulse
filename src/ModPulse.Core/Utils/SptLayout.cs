using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModPulse.Utils;

/// <summary>
/// Which SPT server an installation runs.
/// </summary>
public enum SptServerKind
{
    /// <summary>No SPT server found.</summary>
    Unknown = 0,

    /// <summary>SPT 3.x: the Node/TypeScript server, living in the game root. Server mods use package.json.</summary>
    Node = 1,

    /// <summary>SPT 4.x: the C# server, living in <c>SPT/</c> (4.0) or <c>SPT_Runtime/</c> (4.1). Server mods are DLLs.</summary>
    DotNet = 2,
}

/// <summary>
/// A resolved SPT installation.
/// </summary>
/// <param name="GameRoot">Folder holding EscapeFromTarkov.exe and BepInEx.</param>
/// <param name="ServerRoot">Folder holding the SPT server and its <c>user/mods</c>.</param>
/// <param name="Kind">The server kind.</param>
public sealed record SptInstallation(string GameRoot, string ServerRoot, SptServerKind Kind)
{
    /// <summary>The server mods folder (<c>&lt;server&gt;/user/mods</c>).</summary>
    public string ServerModsDirectory
    {
        get { return Path.Combine(ServerRoot, "user", "mods"); }
    }

    /// <summary>The BepInEx plugins folder (<c>&lt;game&gt;/BepInEx/plugins</c>).</summary>
    public string PluginsDirectory
    {
        get { return Path.Combine(GameRoot, "BepInEx", "plugins"); }
    }
}

/// <summary>
/// The SPT version found on disk and the file it came from.
/// </summary>
public sealed record SptVersionReading(string Version, string Source);

/// <summary>
/// Knows where each supported SPT release keeps its files:
/// <list type="bullet">
/// <item>3.9 / 3.10 / 3.11: server in the game root, version in <c>SPT_Data/Server/configs/core.json</c>.</item>
/// <item>4.0: server in <c>SPT/</c>, version in <c>SPTarkov.Server.Core.dll</c>.</item>
/// <item>4.1: server in <c>SPT_Runtime/</c>, version in <c>SPTarkov.Server.Core.dll</c>.</item>
/// </list>
/// </summary>
public static partial class SptLayout
{
    /// <summary>Oldest supported SPT version.</summary>
    public static readonly SemanticVersioning.Version MinSupported = new(3, 9, 0);

    /// <summary>First SPT version past the supported range.</summary>
    public static readonly SemanticVersioning.Version MaxExclusive = new(4, 2, 0);

    /// <summary>SPT versions below this are legacy (3.9.x and 3.10.x).</summary>
    public static readonly SemanticVersioning.Version LegacyBelow = new(3, 11, 0);

    /// <summary>Human-readable supported range.</summary>
    public const string SupportedRangeText = "3.9.x, 3.10.x, 3.11.x, 4.0.x and 4.1.x";

    /// <summary>Server folder used by SPT 4.0.</summary>
    public const string Spt40ServerFolder = "SPT";

    /// <summary>Server folder used by SPT 4.1.</summary>
    public const string Spt41ServerFolder = "SPT_Runtime";

    private const string CoreDll = "SPTarkov.Server.Core.dll";

    private static readonly string[] _serverFolders = [Spt41ServerFolder, Spt40ServerFolder];

    private static readonly string[] _nodeServerFiles = ["SPT.Server.exe", "SPT.Server", "Aki.Server.exe"];

    /// <summary>
    /// Which server, if any, lives directly in <paramref name="directory"/>.
    /// </summary>
    public static SptServerKind DetectKind(string directory)
    {
        if (File.Exists(Path.Combine(directory, CoreDll)))
        {
            return SptServerKind.DotNet;
        }

        var hasNodeServer =
            _nodeServerFiles.Any(f => File.Exists(Path.Combine(directory, f)))
            || Directory.Exists(Path.Combine(directory, "SPT_Data", "Server"))
            || Directory.Exists(Path.Combine(directory, "Aki_Data", "Server"));

        return hasNodeServer ? SptServerKind.Node : SptServerKind.Unknown;
    }

    /// <summary>
    /// Resolves an installation from whatever folder the user pointed at: the game root, or the 4.x server folder
    /// itself. Returns null when no SPT server is found.
    /// </summary>
    public static SptInstallation? Resolve(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Pointed at a 4.x server folder: the game root is its parent.
        if (DetectKind(full) == SptServerKind.DotNet)
        {
            var parent = Path.GetDirectoryName(full);
            var gameRoot = parent is not null && LooksLikeGameRoot(parent) ? parent : full;
            return new SptInstallation(gameRoot, full, SptServerKind.DotNet);
        }

        // Game root of a 4.x install. If both server folders exist (e.g. SPT/ left behind after upgrading to 4.1),
        // the newer server wins; on a tie the 4.1 folder is preferred.
        var dotNetServers = _serverFolders
            .Select((folder, order) => (Path: Path.Combine(full, folder), Order: order))
            .Where(c => Directory.Exists(c.Path) && DetectKind(c.Path) == SptServerKind.DotNet)
            .ToList();

        if (dotNetServers.Count > 0)
        {
            var newest = dotNetServers
                .OrderByDescending(c => VersionOrZero(c.Path))
                .ThenBy(c => c.Order)
                .First();
            return new SptInstallation(full, newest.Path, SptServerKind.DotNet);
        }

        // 3.x: everything in the game root.
        if (DetectKind(full) == SptServerKind.Node)
        {
            return new SptInstallation(full, full, SptServerKind.Node);
        }

        return null;
    }

    /// <summary>
    /// Reads the installed SPT version from the server folder, normalized to <c>MAJOR.MINOR.PATCH</c>.
    /// </summary>
    public static SptVersionReading? ReadVersion(string serverRoot)
    {
        var kind = DetectKind(serverRoot);

        if (kind == SptServerKind.DotNet)
        {
            // Managed assembly: its version resource is readable on every OS.
            return ReadFileVersion(Path.Combine(serverRoot, CoreDll));
        }

        if (kind == SptServerKind.Node)
        {
            // core.json is authoritative and readable everywhere; the packaged exe's version resource is a
            // fallback that only Windows can read.
            return ReadCoreJsonVersion(serverRoot)
                ?? ReadFileVersion(Path.Combine(serverRoot, "SPT.Server.exe"))
                ?? ReadFileVersion(Path.Combine(serverRoot, "Aki.Server.exe"));
        }

        return null;
    }

    /// <summary>
    /// Reads a file's version resource. Swappable in tests, where real versioned binaries aren't available.
    /// </summary>
    internal static Func<string, string?> FileVersionReader { get; set; } = DefaultFileVersionReader;

    internal static string? DefaultFileVersionReader(string path)
    {
        return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
    }

    private static SptVersionReading? ReadFileVersion(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var version = Normalize(FileVersionReader(path));
        return version is null ? null : new SptVersionReading(version, path);
    }

    private static SptVersionReading? ReadCoreJsonVersion(string serverRoot)
    {
        string[] candidates =
        [
            Path.Combine(serverRoot, "SPT_Data", "Server", "configs", "core.json"),
            Path.Combine(serverRoot, "Aki_Data", "Server", "configs", "core.json"),
        ];

        foreach (var path in candidates.Where(File.Exists))
        {
            try
            {
                using var doc = JsonDocument.Parse(
                    File.ReadAllText(path),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }
                );

                // "sptVersion" since 3.9; older builds used "akiVersion".
                foreach (var key in new[] { "sptVersion", "akiVersion" })
                {
                    if (
                        doc.RootElement.TryGetProperty(key, out var value)
                        && value.ValueKind == JsonValueKind.String
                        && Normalize(value.GetString()) is { } version
                    )
                    {
                        return new SptVersionReading(version, path);
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Unreadable config; fall through to the next source.
            }
        }

        return null;
    }

    private static SemanticVersioning.Version VersionOrZero(string serverRoot)
    {
        var reading = ReadVersion(serverRoot);
        return reading is null ? new SemanticVersioning.Version(0, 0, 0) : new SemanticVersioning.Version(reading.Version);
    }

    private static bool LooksLikeGameRoot(string directory)
    {
        return File.Exists(Path.Combine(directory, "EscapeFromTarkov.exe"))
            || Directory.Exists(Path.Combine(directory, "BepInEx"));
    }

    /// <summary>
    /// Reduces strings like <c>4.1.6-RELEASE+abc</c>, <c>3.10.5.0</c> or <c>v3.9.8</c> to <c>MAJOR.MINOR.PATCH</c>.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var match = VersionPattern().Match(raw);
        if (!match.Success)
        {
            return null;
        }

        var patch = match.Groups[3].Success ? match.Groups[3].Value : "0";
        return $"{match.Groups[1].Value}.{match.Groups[2].Value}.{patch}";
    }

    /// <summary>Whether the version is inside the supported 3.9–4.1 range.</summary>
    public static bool IsSupported(SemanticVersioning.Version version)
    {
        var core = Core(version);
        return core >= MinSupported && core < MaxExclusive;
    }

    /// <summary>Whether the version is legacy (below 3.11).</summary>
    public static bool IsLegacy(SemanticVersioning.Version version)
    {
        return Core(version) < LegacyBelow;
    }

    /// <summary>
    /// The 4.x server folder expected for a version (<c>SPT</c> for 4.0, <c>SPT_Runtime</c> for 4.1+), or null for 3.x.
    /// </summary>
    public static string? ExpectedServerFolder(SemanticVersioning.Version version)
    {
        if (version.Major < 4)
        {
            return null;
        }

        return version is { Major: 4, Minor: 0 } ? Spt40ServerFolder : Spt41ServerFolder;
    }

    /// <summary>
    /// A warning when the server folder doesn't match the version found in it (e.g. 4.1 in <c>SPT/</c>), or null.
    /// Folders with other names are not judged.
    /// </summary>
    public static string? DescribeFolderMismatch(SptInstallation installation, SemanticVersioning.Version version)
    {
        if (installation.Kind != SptServerKind.DotNet)
        {
            return null;
        }

        var folder = Path.GetFileName(installation.ServerRoot);
        if (!_serverFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var expected = ExpectedServerFolder(version);
        if (expected is null)
        {
            return $"Found SPT {version} inside '{folder}/', but SPT 3.x runs from the game root.";
        }

        return string.Equals(folder, expected, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Found SPT {version} inside '{folder}/', but SPT {version.Major}.{version.Minor} uses '{expected}/'. Check this is the server you actually run.";
    }

    private static SemanticVersioning.Version Core(SemanticVersioning.Version version)
    {
        return new SemanticVersioning.Version(version.Major, version.Minor, version.Patch);
    }

    [GeneratedRegex(@"(\d+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex VersionPattern();
}
