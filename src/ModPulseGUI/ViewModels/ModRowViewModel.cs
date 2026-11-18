using CommunityToolkit.Mvvm.ComponentModel;
using ModPulse.Models;
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

    /// <summary>Short tag next to the status: "Addon" or "Bundled", or null for a regular mod.</summary>
    public string? RelationTag
    {
        get { return Mod.Addon is not null ? "Addon" : Mod.PartOf is not null ? "Bundled" : null; }
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

    /// <summary>Only regular Forge updates can be ignored (the ignore list is keyed by Forge mod ID).</summary>
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
            var latest = Mod.Addon?.LatestVersion ?? Mod.LatestVersion;
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
    [NotifyPropertyChangedFor(nameof(CanUnignore))]
    private IgnoreSource? _ignoreSource;

    /// <summary>
    /// Built-in community entries ship inside ModPulse and can't be removed; local and fetched entries can.
    /// </summary>
    public bool CanUnignore
    {
        get { return StatusKind == ModStatusKind.Ignored && IgnoreSource is not ModPulse.Models.IgnoreSource.BuiltIn; }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnignore), nameof(CanIgnore))]
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
        OnPropertyChanged(nameof(DownloadUrl));
        OnPropertyChanged(nameof(LatestVersion));
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

        if (!mod.IsMatched)
        {
            return (ModStatusKind.NotOnForge, "Not on Forge", "No matching Forge listing was found for this mod.");
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
