using ModPulse.Models;

namespace ModPulse.Services.Interfaces;

/// <summary>
/// The GitHub links for off-Forge mods: the user's own (<c>github-sources.json</c>) over the built-in community list.
/// </summary>
public interface IGitHubSourceStore
{
    /// <summary>All links in effect: the user's, then built-in ones for mods the user hasn't linked.</summary>
    IReadOnlyList<GitHubSource> Load();

    /// <summary>The link for a mod (the user's wins over a built-in one), or null.</summary>
    GitHubSource? Find(Mod mod);

    /// <summary>Links (or re-links) a mod to <paramref name="repo"/> (<c>owner/repo</c>) in the user's file.</summary>
    void Link(Mod mod, string repo);

    /// <summary>
    /// Removes the user's link for a mod. Returns false when there was none (a built-in link can't be removed, only
    /// overridden).
    /// </summary>
    bool Unlink(Mod mod);
}
