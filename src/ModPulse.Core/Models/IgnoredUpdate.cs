using System.Text.Json.Serialization;

namespace ModPulse.Models;

/// <summary>
/// Where an <see cref="IgnoredUpdate"/> entry came from.
/// </summary>
public enum IgnoreSource
{
    /// <summary>The user dismissed this update locally.</summary>
    User,

    /// <summary>The entry was merged in from the author-maintained remote base list.</summary>
    Remote,

    /// <summary>
    /// The entry ships inside ModPulse (the repository's community list at build time). It always applies, needs no
    /// download, and is never written to the local file.
    /// </summary>
    BuiltIn,
}

/// <summary>
/// A single dismissed ("ignored") mod update. Identifies a known false-positive update where the Forge version is
/// higher than the installed DLL version but the distributed files are actually the same. Matching uses the triple
/// (<see cref="ApiModId"/>, <see cref="LocalVersion"/>, <see cref="IgnoredLatestVersion"/>); the remaining fields are
/// metadata.
/// </summary>
public sealed record IgnoredUpdate(
    [property: JsonPropertyName("apiModId")] int ApiModId,
    [property: JsonPropertyName("localVersion")] string LocalVersion,
    [property: JsonPropertyName("ignoredLatestVersion")] string IgnoredLatestVersion,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("guid")] string? Guid = null,
    [property: JsonPropertyName("source")] IgnoreSource Source = IgnoreSource.User,
    [property: JsonPropertyName("dismissedUtc")] DateTimeOffset? DismissedUtc = null,
    [property: JsonPropertyName("github")] string? GitHubRepo = null
)
{
    /// <summary>
    /// The match key: the Forge mod id (or <c>github:owner/repo</c> for an off-Forge mod's GitHub update) plus both
    /// version strings. Compared with <see cref="StringComparer.OrdinalIgnoreCase"/> by callers.
    /// </summary>
    [JsonIgnore]
    public string Key
    {
        get { return MakeKey(ApiModId, GitHubRepo, LocalVersion, IgnoredLatestVersion); }
    }

    /// <summary>
    /// Which mod's update this entry is about: the Forge mod id, or <c>github:owner/repo</c> (see <see cref="IdentityOf"/>).
    /// </summary>
    [JsonIgnore]
    public string ModIdentity
    {
        get { return IsGitHub ? "github:" + GitHubRepo : ApiModId.ToString(); }
    }

    /// <summary>
    /// The identity of a mod's current update (matching <see cref="ModIdentity"/>), or null when it has none to ignore.
    /// </summary>
    public static string? IdentityOf(Mod mod)
    {
        return For(mod)?.ModIdentity;
    }

    /// <summary>True for an ignored GitHub release of an off-Forge mod (no Forge id).</summary>
    [JsonIgnore]
    public bool IsGitHub
    {
        get { return !string.IsNullOrWhiteSpace(GitHubRepo); }
    }

    /// <summary>Builds a match key; see <see cref="Key"/>.</summary>
    public static string MakeKey(int apiModId, string? gitHubRepo, string localVersion, string latestVersion)
    {
        var id = string.IsNullOrWhiteSpace(gitHubRepo) ? apiModId.ToString() : "github:" + gitHubRepo;
        return $"{id}|{localVersion}|{latestVersion}";
    }

    /// <summary>
    /// The entry that ignores <paramref name="mod"/>'s current update: its Forge update, or its GitHub release when the
    /// mod is off the Forge. Null when the mod has no update to ignore.
    /// </summary>
    public static IgnoredUpdate? For(Mod mod, IgnoreSource source = IgnoreSource.User, DateTimeOffset? dismissedUtc = null)
    {
        var guid = string.IsNullOrWhiteSpace(mod.Guid) ? null : mod.Guid;
        if (mod is { ApiModId: { } id, LatestVersion: { } latest })
        {
            return new IgnoredUpdate(id, mod.LocalVersion, latest, mod.DisplayName, guid, source, dismissedUtc);
        }

        if (mod.GitHub is { Version: { } version } github && !mod.IsMatched)
        {
            return new IgnoredUpdate(0, mod.LocalVersion, version, mod.DisplayName, guid, source, dismissedUtc, github.Repo);
        }

        return null;
    }

    /// <summary>
    /// Whether this entry carries the minimum data needed to match a mod.
    /// </summary>
    [JsonIgnore]
    public bool IsWellFormed
    {
        get
        {
            return (ApiModId > 0 || IsGitHub)
                && !string.IsNullOrWhiteSpace(LocalVersion)
                && !string.IsNullOrWhiteSpace(IgnoredLatestVersion);
        }
    }
}

/// <summary>
/// The on-disk (and remote) document format for the ignored-updates list.
/// </summary>
public sealed record IgnoredUpdatesFile(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("ignored")] List<IgnoredUpdate> Ignored
)
{
    /// <summary>The schema version this build reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;
}
