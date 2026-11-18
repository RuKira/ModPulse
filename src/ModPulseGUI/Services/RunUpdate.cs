namespace ModPulse.Gui.Services;

/// <summary>Severity of a line in the run's activity log.</summary>
public enum RunLogLevel
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>A live update from a running check, delivered on the UI thread through <see cref="IProgress{T}"/>.</summary>
public abstract record RunUpdate;

/// <summary>The check moved on to a new step (e.g. "Verifying Forge records for 158 mods").</summary>
public sealed record StageChanged(string Title) : RunUpdate;

/// <summary>A message for the activity log.</summary>
public sealed record LogAdded(RunLogLevel Level, string Text) : RunUpdate;

/// <summary>Progress through a Forge-bound step.</summary>
public sealed record ProgressChanged(int Value, int Total) : RunUpdate;
