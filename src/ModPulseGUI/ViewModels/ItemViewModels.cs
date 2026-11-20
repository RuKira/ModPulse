using ModPulse.Gui.Services;
using ModPulse.Models;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;

namespace ModPulse.Gui.ViewModels;

/// <summary>One line in the activity log.</summary>
public sealed record LogEntry(DateTime Time, RunLogLevel Level, string Text)
{
    public string TimeText
    {
        get { return Time.ToString("HH:mm:ss"); }
    }
}

/// <summary>What kind of install problem a <see cref="ProblemItem"/> is.</summary>
public enum ProblemKind
{
    WrongFolder,
    CrossInstalled,
    LoadWarning,
    PairingNote,
}

/// <summary>An install problem: a misplaced mod, a DLL that loaded with warnings, a server/client pairing note.</summary>
public sealed record ProblemItem(ProblemKind Kind, string Title, string Detail, string? Path)
{
    public string KindText
    {
        get
        {
            return Kind switch
            {
                ProblemKind.WrongFolder => "Wrong folder",
                ProblemKind.CrossInstalled => "Mixed folder",
                ProblemKind.LoadWarning => "Load warning",
                _ => "Pairing",
            };
        }
    }

    /// <summary>Misplaced mods are skipped by the other checks, so they matter most.</summary>
    public bool IsSevere
    {
        get { return Kind is ProblemKind.WrongFolder or ProblemKind.CrossInstalled; }
    }

    /// <summary>Builds the problem list from a check result.</summary>
    public static List<ProblemItem> FromResult(ModCheckResult result)
    {
        var items = new List<ProblemItem>();

        if (result.Misplaced is { } misplaced)
        {
            foreach (var mod in misplaced.WrongFolder)
            {
                var detail = mod.IsServerMod
                    ? "Server mod in BepInEx/plugins. Move it to user/mods in the SPT server folder. Skipped by this check."
                    : "Client mod in the server's user/mods. Move it to BepInEx/plugins. Skipped by this check.";
                items.Add(new ProblemItem(ProblemKind.WrongFolder, $"{mod.Name} {mod.Version}", detail, mod.FilePath));
            }

            foreach (var directory in misplaced.CrossInstalled)
            {
                var names = string.Join(", ", directory.Misplaced.Select(m => m.Name));
                var detail = directory.Ambiguous
                    ? "Unrelated mods share this plugins folder and ModPulse can't tell which one belongs there. The whole folder was skipped; check each mod's install instructions."
                    : $"Contains another mod that likely belongs in its own folder ({names}). It was skipped; check its install instructions.";
                items.Add(
                    new ProblemItem(
                        ProblemKind.CrossInstalled,
                        System.IO.Path.GetFileName(directory.Directory.TrimEnd('\\', '/')),
                        detail,
                        directory.Directory
                    )
                );
            }
        }

        foreach (var mod in result.ModsWithLoadWarnings)
        {
            items.Add(
                new ProblemItem(
                    ProblemKind.LoadWarning,
                    mod.DisplayName,
                    string.Join(" ", mod.LoadWarnings),
                    mod.FilePath
                )
            );
        }

        if (result.Reconciliation is { } reconciliation)
        {
            foreach (var pair in reconciliation.ReconciledPairs.Where(p => p.Notes.Count > 0))
            {
                items.Add(
                    new ProblemItem(
                        ProblemKind.PairingNote,
                        pair.SelectedMod.DisplayName,
                        string.Join(" ", pair.Notes),
                        pair.SelectedMod.FilePath
                    )
                );
            }
        }

        return items;
    }
}

/// <summary>A dependency that's required but not installed.</summary>
public sealed record MissingDependencyItem(
    string Name,
    string Version,
    string? ForgeUrl,
    string? DownloadUrl,
    string? RequiredBy = null
)
{
    /// <summary>One line under the name: what's needed, and who needs it.</summary>
    public string Detail
    {
        get
        {
            var need = !string.IsNullOrWhiteSpace(Version)
                ? $"Needs version {Version}"
                : ForgeUrl is null
                    ? "Not found on the Forge"
                    : "No version on the Forge supports your SPT";
            return RequiredBy is null ? need : $"{need} · required by {RequiredBy}";
        }
    }

    public static MissingDependencyItem From(MissingDependency missing)
    {
        return new MissingDependencyItem(
            missing.Name,
            missing.RecommendedVersion,
            missing.ModId > 0 ? ForgeUrls.ModPage(missing.ModId, missing.Slug) : null,
            missing.DownloadLink,
            missing.RequiredBy
        );
    }
}

/// <summary>An installed dependency whose version doesn't satisfy what a mod needs.</summary>
public sealed record DependencyConflictItem(string ModName, string Description);

/// <summary>A node in the dependency tree.</summary>
public sealed class DependencyNodeItem
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public bool IsInstalled { get; init; } = true;

    public List<DependencyNodeItem> Children { get; init; } = [];

    public string StateText
    {
        get { return IsInstalled ? string.Empty : "missing"; }
    }

    /// <summary>
    /// Builds the tree, keeping only mods that have dependencies (a flat list of 150 dependency-free mods is noise).
    /// </summary>
    public static List<DependencyNodeItem> FromResult(DependencyAnalysisResult? result)
    {
        if (result is null)
        {
            return [];
        }

        return result.RootMods.Where(r => r.Children.Count > 0).Select(Convert).OrderBy(n => n.Name).ToList();
    }

    private static DependencyNodeItem Convert(DependencyNode node)
    {
        return new DependencyNodeItem
        {
            Name = node.IsInstalled ? node.Mod.DisplayName : node.DependencyInfo?.Name ?? node.Mod.DisplayName,
            Version = node.IsInstalled
                ? node.Mod.LocalVersion
                : node.DependencyInfo?.LatestCompatibleVersion?.Version ?? string.Empty,
            IsInstalled = node.IsInstalled,
            Children = node.Children.Select(Convert).ToList(),
        };
    }
}
