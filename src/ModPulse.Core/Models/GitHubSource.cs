using System.Text.Json.Serialization;
using ModPulse.Utils;

namespace ModPulse.Models;

/// <summary>Where a <see cref="GitHubSource"/> link came from.</summary>
public enum GitHubSourceOrigin
{
    /// <summary>The user linked the mod (stored in their <c>github-sources.json</c>).</summary>
    User,

    /// <summary>Ships inside ModPulse (the repository's <c>community/github-sources.json</c> at build time).</summary>
    BuiltIn,
}

/// <summary>
/// Links an installed mod that isn't on the Forge to a GitHub repository whose releases are checked for updates.
/// </summary>
/// <param name="Key">
/// The mod's GUID, or <c>name:&lt;normalized local name&gt;</c> for a mod without one (see <see cref="KeyFor"/>).
/// </param>
/// <param name="Repo">The repository as <c>owner/repo</c>.</param>
/// <param name="Name">The mod's name when it was linked (for people reading the file).</param>
/// <param name="Origin">Where the link came from (not stored in the file).</param>
public sealed record GitHubSource(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("repo")] string Repo,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonIgnore] GitHubSourceOrigin Origin = GitHubSourceOrigin.User
)
{
    /// <summary>Whether the entry has a key and a valid <c>owner/repo</c>.</summary>
    [JsonIgnore]
    public bool IsWellFormed
    {
        get { return !string.IsNullOrWhiteSpace(Key) && GitHubRepoName.TryParse(Repo, out _); }
    }

    /// <summary>
    /// The link key for a mod: its GUID, or its normalized local name when it has no GUID (3.x server mods can lack
    /// one).
    /// </summary>
    public static string KeyFor(Mod mod)
    {
        return !string.IsNullOrWhiteSpace(mod.Guid)
            ? mod.Guid.Trim()
            : "name:" + ModNameNormalizer.Normalize(mod.LocalName);
    }
}

/// <summary>The on-disk document for GitHub links (local file and the built-in community list).</summary>
public sealed record GitHubSourcesFile(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("sources")] List<GitHubSource> Sources
)
{
    /// <summary>The schema version this build reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;
}
