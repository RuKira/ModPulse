using System.Text.Json.Serialization;

namespace ModPulse.Models;

/// <summary>One version of a Forge addon. Its constraint is against the parent mod's version, not SPT.</summary>
public record AddonVersion(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("link")] string? Link,
    [property: JsonPropertyName("mod_version_constraint")] string? ModVersionConstraint
);

/// <summary>
/// A Forge addon: an add-on to a specific mod (its <see cref="ModId"/>). Addons have no GUID on the Forge, so they are
/// found through their parent mod.
/// </summary>
public record AddonResult(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("mod_id")] int? ModId,
    [property: JsonPropertyName("detail_url")] string? DetailUrl,
    [property: JsonPropertyName("versions")] List<AddonVersion>? Versions
);

/// <summary>Response of <c>addons?filter[mod_id]=…</c>.</summary>
public record AddonListApiResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("data")] List<AddonResult>? Data
);

/// <summary>Where an installed addon stands against its Forge listing and its parent mod.</summary>
public enum AddonStatus
{
    /// <summary>The installed version is the newest one that fits the installed parent.</summary>
    UpToDate,

    /// <summary>A newer version that fits the installed parent exists.</summary>
    UpdateAvailable,

    /// <summary>The installed version is newer than anything on the Forge.</summary>
    NewerInstalled,

    /// <summary>The installed version needs a different version of the parent mod.</summary>
    IncompatibleWithParent,
}

/// <summary>An installed plugin identified as a Forge addon of an installed mod.</summary>
/// <param name="Addon">The Forge addon listing.</param>
/// <param name="Parent">The installed parent mod.</param>
/// <param name="Status">Where the installed version stands.</param>
/// <param name="LatestVersion">The newest version that fits the installed parent (or the newest overall).</param>
/// <param name="DownloadLink">Download for <paramref name="LatestVersion"/> when it is worth installing.</param>
/// <param name="Reason">One line explaining <paramref name="Status"/>.</param>
public sealed record AddonMatch(
    AddonResult Addon,
    Mod Parent,
    AddonStatus Status,
    string? LatestVersion,
    string? DownloadLink,
    string Reason
);
