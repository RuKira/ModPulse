using ModPulse.Services.Interfaces;
using ModPulse.Utils;

namespace ModPulse.Models;

/// <summary>How a mod check ended.</summary>
public enum ModCheckOutcome
{
    /// <summary>Every step ran.</summary>
    Completed,

    /// <summary>The path was invalid or doesn't exist.</summary>
    InvalidPath,

    /// <summary>No SPT install was found or its version couldn't be read/validated.</summary>
    SptNotValidated,

    /// <summary>The install has no mods (or none left after excluding misplaced ones).</summary>
    NoMods,

    /// <summary>The run was cancelled.</summary>
    Cancelled,

    /// <summary>An unexpected error stopped the run (see <see cref="ModCheckResult.Error"/>).</summary>
    Failed,
}

/// <summary>
/// Everything a mod check found. Front ends render this; the CLI also prints live as it goes. Fields stay null for
/// steps the run never reached.
/// </summary>
public sealed class ModCheckResult
{
    /// <summary>How the run ended.</summary>
    public ModCheckOutcome Outcome { get; internal set; } = ModCheckOutcome.Completed;

    /// <summary>The exception behind <see cref="ModCheckOutcome.Failed"/>.</summary>
    public Exception? Error { get; internal set; }

    /// <summary>The resolved game root that was checked.</summary>
    public string? GamePath { get; internal set; }

    /// <summary>The detected SPT layout (server folder and kind).</summary>
    public SptInstallation? Installation { get; internal set; }

    /// <summary>The installed SPT version.</summary>
    public SemanticVersioning.Version? SptVersion { get; internal set; }

    /// <summary>
    /// The SPT version used for Forge queries: the installed one, or the newest same-minor patch the Forge lists when it
    /// doesn't list the installed one (SPT 3.11.5 -> 3.11.4).
    /// </summary>
    public SemanticVersioning.Version? ForgeSptVersion { get; internal set; }

    /// <summary>True for SPT below 3.11 (legacy rules applied).</summary>
    public bool IsLegacy
    {
        get { return SptVersion is not null && SptLayout.IsLegacy(SptVersion); }
    }

    /// <summary>The newest SPT release, when newer than the installed one.</summary>
    public SptVersionResult? SptUpdate { get; internal set; }

    /// <summary>The ModPulse self-update check.</summary>
    public ModPulseUpdateResult? ModPulseUpdate { get; internal set; }

    /// <summary>Mods installed in the wrong place (excluded from the other checks).</summary>
    public MisplacedModReport? Misplaced { get; internal set; }

    /// <summary>Mods whose files loaded with warnings.</summary>
    public IReadOnlyList<Mod> ModsWithLoadWarnings { get; internal set; } = [];

    /// <summary>How server and client components were paired.</summary>
    public ModReconciliationResult? Reconciliation { get; internal set; }

    /// <summary>The final mods: matched, enriched, legacy-labeled, ignores applied, compatibility checked.</summary>
    public IReadOnlyList<Mod> Mods { get; internal set; } = [];

    /// <summary>The dependency tree and any missing/conflicting dependencies.</summary>
    public DependencyAnalysisResult? Dependencies { get; internal set; }
}
