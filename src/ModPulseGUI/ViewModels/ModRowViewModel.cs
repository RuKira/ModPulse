using CommunityToolkit.Mvvm.ComponentModel;
using ModPulse.Models;
using ModPulse.Services;
using ModPulse.Utils;

namespace ModPulse.Gui.ViewModels;

/// <summary>What the status pill shows, in priority order (most urgent first).</summary>
public enum ModStatusKind
{
    Incompatible,
    Update,
    Blocked,
    NotOnForge,
    Ignored,
    Newer,
    UpToDate,
    Bundled,
    Unknown,
}

/// <summary>One installed mod as the GUI shows it. Wraps the Core <see cref="Mod"/> without changing it.</summary>
public sealed partial class ModRowViewModel : ObservableObject
{
    public ModRowViewModel(Mod mod)
    {
        Mod = mod;
        Refresh();
    }

    /// <summary>The underlying Core mod.</summary>
    public Mod Mod { get; }

    /// <summary>The Forge addon's name for an addon, otherwise the mod's display name.</summary>
    public string Name
    {
        get { return Mod.Addon?.Addon.Name ?? Mod.DisplayName; }
    }

    public bool IsAddon
    {
        get { return Mod.Addon is not null; }
    }

    /// <summary>
    /// Short tag next to the status: "Addon", "Bundled", or "GitHub" for an off-Forge mod linked to a repository; null
    /// for a regular mod.
    /// </summary>
    public string? RelationTag
    {
        get
        {
            return Mod.Addon is not null ? "Addon"
                : Mod.PartOf is not null ? "Bundled"
                : IsGitHubLinked ? "GitHub"
                : null;
        }
    }

    /// <summary>True for a mod that isn't on the Forge and isn't part of another mod (it can be linked to GitHub).</summary>
    public bool IsOffForge
    {
        get { return !Mod.IsMatched && Mod.Addon is null && Mod.PartOf is null; }
    }

    /// <summary>The GitHub verdict for an off-Forge mod linked to a repository.</summary>
    public GitHubMatch? GitHub
    {
        get { return IsOffForge ? Mod.GitHub : null; }
    }

    public bool IsGitHubLinked
    {
        get { return GitHub is not null; }
    }

    /// <summary>Any off-Forge mod can be linked (a built-in community link can be overridden by the user's own).</summary>
    public bool CanLinkGitHub
    {
        get { return IsOffForge; }
    }

    /// <summary>Only the user's own links can be removed; built-in community links can only be replaced.</summary>
    public bool CanUnlinkGitHub
    {
        get { return GitHub?.Origin == GitHubSourceOrigin.User; }
    }

    public string LinkGitHubLabel
    {
        get { return IsGitHubLinked ? "Change GitHub repo" : "Link GitHub repo"; }
    }

    /// <summary>"owner/repo · linked by you" or "owner/repo · community list".</summary>
    public string? GitHubText
    {
        get
        {
            return GitHub is { } gh
                ? $"{gh.Repo} · {(gh.Origin == GitHubSourceOrigin.User ? "linked by you" : "community list")}"
                : null;
        }
    }

    /// <summary>The latest release's tag and date, when one was found.</summary>
    public string? GitHubReleaseText
    {
        get
        {
            if (GitHub?.Release is not { } release)
            {
                return null;
            }

            var kind = release.FromTags ? "Latest tag" : "Latest release";
            var date = release.PublishedUtc is { } published ? $" · {published.ToLocalTime():yyyy-MM-dd}" : string.Empty;
            return $"{kind} {release.Tag}{date}";
        }
    }

    public string? GitHubRepoUrl
    {
        get { return GitHub?.RepoUrl; }
    }

    public string? GitHubReleaseUrl
    {
        get { return GitHub?.Release is not null ? GitHub.ReleaseUrl : null; }
    }

    /// <summary>The page button in the Updates list: the Forge page, or the GitHub repository.</summary>
    public string? PageUrl
    {
        get { return ForgeUrl ?? GitHubRepoUrl; }
    }

    public string PageLabel
    {
        get { return ForgeUrl is null && GitHubRepoUrl is not null ? "GitHub" : "Forge page"; }
    }

    /// <summary>
    /// A version mismatch the user ignored that the built-in community list doesn't have yet, so it can be reported.
    /// </summary>
    public bool CanReportMismatch
    {
        get
        {
            return StatusKind == ModStatusKind.Ignored
                && IgnoreSource == ModPulse.Models.IgnoreSource.User
                && IgnoredUpdate.For(Mod) is { } entry
                && !BuiltInIgnoreList.Keys.Contains(entry.Key);
        }
    }

    /// <summary>"Addon of ORBIT 3.0 3.1.1" / "Part of Show Me The Money", or null for a regular mod.</summary>
    public string? RelationText
    {
        get
        {
            if (Mod.Addon is { } addon)
            {
                return $"Addon of {addon.Parent.DisplayName} {addon.Parent.LocalVersion}";
            }

            return Mod.PartOf is { } parent ? $"Part of {parent.DisplayName} (ships in its download)" : null;
        }
    }

    /// <summary>Forge updates and GitHub releases of linked off-Forge mods can be ignored; addons can't.</summary>
    public bool CanIgnore
    {
        get { return StatusKind == ModStatusKind.Update && !IsAddon; }
    }

    public string Author
    {
        get { return Mod.DisplayAuthor; }
    }

    public string Guid
    {
        get { return Mod.Guid; }
    }

    /// <summary>Server, Client, or Server + Client when both halves were found and paired.</summary>
    public string Side
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Mod.PairedComponentPath))
            {
                return "Server + Client";
            }

            return Mod.IsServerMod ? "Server" : "Client";
        }
    }

    public string InstalledVersion
    {
        get { return Mod.LocalVersion; }
    }

    /// <summary>Forge's latest version for this SPT, or a dash when unknown.</summary>
    public string LatestVersion
    {
        get
        {
            var latest = Mod.Addon?.LatestVersion ?? Mod.LatestVersion ?? GitHub?.Version;
            return string.IsNullOrWhiteSpace(latest) ? "—" : latest!;
        }
    }

    public bool IsLegacy
    {
        get { return Mod.IsLegacy; }
    }

    public string FilePath
    {
        get { return Mod.FilePath; }
    }

    public string? PairedFilePath
    {
        get { return Mod.PairedComponentPath; }
    }

    public bool IsOnForge
    {
        get { return Mod.IsMatched; }
    }

    /// <summary>The Forge mod page, when matched.</summary>
    public string? ForgeUrl
    {
        get
        {
            if (Mod.Addon is { } addon)
            {
                return addon.Addon.DetailUrl ?? ForgeUrls.ModPage(addon.Parent.ApiModId ?? 0, addon.Parent.ApiSlug);
            }

            if (Mod.PartOf is { } parent)
            {
                return parent.ApiUrl
                    ?? (parent.ApiModId is { } parentId ? ForgeUrls.ModPage(parentId, parent.ApiSlug) : null);
            }

            if (!string.IsNullOrWhiteSpace(Mod.ApiUrl))
            {
                return Mod.ApiUrl;
            }

            return Mod.ApiModId is { } id ? ForgeUrls.ModPage(id, Mod.ApiSlug) : null;
        }
    }

    /// <summary>The download for the version worth installing: the update, or the latest compatible one.</summary>
    public string? DownloadUrl
    {
        get
        {
            if (Mod.Addon is { } addon)
            {
                return addon.DownloadLink;
            }

            if (GitHub is { } github)
            {
                return StatusKind is ModStatusKind.Update or ModStatusKind.Ignored ? github.ReleaseUrl : null;
            }

            if (Mod.ApiModId is not { } id)
            {
                return null;
            }

            if (StatusKind == ModStatusKind.Incompatible && !string.IsNullOrWhiteSpace(Mod.CompatibleVersionString))
            {
                return ForgeUrls.Download(id, Mod.ApiSlug, Mod.CompatibleVersionString);
            }

            if (StatusKind is ModStatusKind.Update or ModStatusKind.Ignored)
            {
                return !string.IsNullOrWhiteSpace(Mod.DownloadLink)
                    ? Mod.DownloadLink
                    : ForgeUrls.Download(id, Mod.ApiSlug, Mod.LatestVersion);
            }

            return null;
        }
    }

    public string? SourceCodeUrl
    {
        get { return Mod.ApiSourceCodeUrl; }
    }

    /// <summary>Forge's SPT constraint for the installed version, when Forge lists that version.</summary>
    public string? InstalledSptConstraint
    {
        get
        {
            return Mod.ApiVersions
                ?.FirstOrDefault(v => string.Equals(v.Version, Mod.LocalVersion, StringComparison.OrdinalIgnoreCase))
                ?.SptVersionConstraint;
        }
    }

    public string? CompatibleVersion
    {
        get { return Mod.CompatibleVersionString; }
    }

    /// <summary>Every warning the scanner raised while loading this mod.</summary>
    public IReadOnlyList<string> LoadWarnings
    {
        get { return Mod.LoadWarnings; }
    }

    public string? LegacyNote
    {
        get { return Mod.LegacyNote; }
    }

    /// <summary>Dependencies an update would add or drop.</summary>
    public string? UpdateDependencyNote
    {
        get
        {
            var changes = Mod.UpdateDependencyChanges;
            if (changes is null || !changes.HasChanges)
            {
                return null;
            }

            var parts = new List<string>();
            if (changes.Added.Count > 0)
            {
                parts.Add("adds " + string.Join(", ", changes.Added.Select(c => c.Name)));
            }

            if (changes.Removed.Count > 0)
            {
                parts.Add("drops " + string.Join(", ", changes.Removed.Select(c => c.Name)));
            }

            return "This update " + string.Join("; ", parts) + ".";
        }
    }

    /// <summary>Where the ignore entry for this mod's update comes from (null when not ignored).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnignore), nameof(CanReportMismatch))]
    private IgnoreSource? _ignoreSource;

    /// <summary>
    /// Built-in community entries ship inside ModPulse and can't be removed; local and fetched entries can.
    /// </summary>
    public bool CanUnignore
    {
        get { return StatusKind == ModStatusKind.Ignored && IgnoreSource is not ModPulse.Models.IgnoreSource.BuiltIn; }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnignore), nameof(CanIgnore), nameof(CanReportMismatch))]
    private ModStatusKind _statusKind;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>One line explaining the status (shown in the grid and the details pane).</summary>
    [ObservableProperty]
    private string _reason = string.Empty;

    /// <summary>Recomputes the status after something changed (e.g. the update was ignored).</summary>
    public void Refresh()
    {
        (StatusKind, StatusText, Reason) = Describe(Mod, IgnoreSource);
        foreach (
            var name in new[]
            {
                nameof(DownloadUrl), nameof(LatestVersion), nameof(GitHub), nameof(IsGitHubLinked),
                nameof(CanUnlinkGitHub), nameof(LinkGitHubLabel), nameof(GitHubText), nameof(GitHubReleaseText),
                nameof(GitHubRepoUrl), nameof(GitHubReleaseUrl), nameof(PageUrl), nameof(PageLabel),
                nameof(RelationTag), nameof(CanReportMismatch),
            }
        )
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>Turns Forge's machine reason codes into a sentence.</summary>
    internal static string FriendlyForgeReason(string? reason)
    {
        return reason switch
        {
            null or "" => "Forge reports this mod is incompatible with your SPT version.",
            "no_version_for_spt" => "Forge has no version of this mod for your SPT version.",
            _ when reason.Contains('_') && !reason.Contains(' ') => reason.Replace('_', ' ') + ".",
            _ => reason,
        };
    }

    /// <summary>Maps a Core mod to the status shown in the GUI.</summary>
    internal static (ModStatusKind Kind, string Text, string Reason) Describe(Mod mod, IgnoreSource? ignoreSource = null)
    {
        if (mod.Addon is { } addon)
        {
            return addon.Status switch
            {
                AddonStatus.UpdateAvailable => (ModStatusKind.Update, "Update", addon.Reason),
                AddonStatus.IncompatibleWithParent => (ModStatusKind.Incompatible, "Incompatible", addon.Reason),
                AddonStatus.NewerInstalled => (ModStatusKind.Newer, "Newer", addon.Reason),
                _ => (ModStatusKind.UpToDate, "Up to date", addon.Reason),
            };
        }

        if (mod.PartOf is { } parent)
        {
            return (ModStatusKind.Bundled, "Bundled", $"Ships inside {parent.DisplayName}; it updates with that mod.");
        }

        if (mod.IsLocalSptIncompatible)
        {
            var hint = string.IsNullOrWhiteSpace(mod.CompatibleVersionString)
                ? "No version on Forge supports this SPT yet."
                : $"Version {mod.CompatibleVersionString} supports this SPT.";
            return (ModStatusKind.Incompatible, "Incompatible", $"{mod.IncompatibilityReason}. {hint}");
        }

        switch (mod.UpdateStatus)
        {
            case UpdateStatus.UpdateAvailable when mod.UpdateSuppressed:
                return (
                    ModStatusKind.Ignored,
                    "Ignored",
                    ignoreSource is ModPulse.Models.IgnoreSource.BuiltIn or ModPulse.Models.IgnoreSource.Remote
                        ? $"Known version mismatch (community list): the mod's DLL reports {mod.LocalVersion}, but it is Forge's {mod.LatestVersion}."
                        : $"Update to {mod.LatestVersion} ignored by you (marked as the same files as the installed version)."
                );

            case UpdateStatus.UpdateAvailable:
                return (ModStatusKind.Update, "Update", $"{mod.LocalVersion} → {mod.LatestVersion}");

            case UpdateStatus.UpdateBlocked:
                return (
                    ModStatusKind.Blocked,
                    "Blocked",
                    string.IsNullOrWhiteSpace(mod.BlockReason)
                        ? $"Update to {mod.LatestVersion} is blocked by another installed mod."
                        : mod.BlockReason!
                );

            case UpdateStatus.Incompatible:
                return (ModStatusKind.Incompatible, "Incompatible", FriendlyForgeReason(mod.IncompatibilityReason));
        }

        if (!mod.IsMatched && mod.GitHub is { } github)
        {
            return github.Status switch
            {
                GitHubMatchStatus.UpdateAvailable when mod.UpdateSuppressed => (
                    ModStatusKind.Ignored,
                    "Ignored",
                    ignoreSource is ModPulse.Models.IgnoreSource.BuiltIn or ModPulse.Models.IgnoreSource.Remote
                        ? $"Known version mismatch (community list): the mod reports {mod.LocalVersion}, but it is GitHub's {github.Version}."
                        : $"GitHub release {github.Version} ignored by you (marked as the same files as the installed version)."
                ),
                GitHubMatchStatus.UpdateAvailable => (ModStatusKind.Update, "Update", github.Reason),
                GitHubMatchStatus.UpToDate => (ModStatusKind.UpToDate, "Up to date", github.Reason),
                GitHubMatchStatus.NewerInstalled => (ModStatusKind.Newer, "Newer", github.Reason),
                _ => (ModStatusKind.NotOnForge, "Off Forge", github.Reason),
            };
        }

        if (!mod.IsMatched)
        {
            return (
                ModStatusKind.NotOnForge,
                "Off Forge",
                "No matching Forge listing was found for this mod. If it's published on GitHub, link its repository to check it for updates."
            );
        }

        return mod.UpdateStatus switch
        {
            UpdateStatus.UpToDate when mod.IsLegacy && !string.IsNullOrWhiteSpace(mod.LegacyNote) => (
                ModStatusKind.UpToDate,
                "Up to date",
                mod.LegacyNote!
            ),
            UpdateStatus.UpToDate => (ModStatusKind.UpToDate, "Up to date", "Latest version for your SPT."),
            UpdateStatus.NewerInstalled => (
                ModStatusKind.Newer,
                "Newer",
                $"Installed {mod.LocalVersion} is newer than Forge's {mod.LatestVersion}."
            ),
            UpdateStatus.NoVersionsFound => (
                ModStatusKind.Unknown,
                "Unknown",
                "Forge has no versions of this mod for your SPT."
            ),
            _ => (ModStatusKind.Unknown, "Unknown", "Forge didn't report a status for this mod."),
        };
    }
}
