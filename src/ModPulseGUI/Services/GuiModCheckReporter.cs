using ModPulse.Models;
using ModPulse.Services.Interfaces;

namespace ModPulse.Gui.Services;

/// <summary>
/// The GUI's side of <see cref="IModCheckReporter"/>. The pipeline calls it as it goes; this turns those calls into
/// <see cref="RunUpdate"/>s (current step, progress, activity log). It deliberately ignores the "display results"
/// calls: the GUI shows results from the returned <see cref="ModCheckResult"/> instead.
/// </summary>
/// <param name="updates">Where live updates go (a WPF <see cref="Progress{T}"/> marshals them to the UI thread).</param>
/// <param name="fetchCommunityIgnores">The answer to "fetch the community ignore list?", from the GUI's settings.</param>
public sealed class GuiModCheckReporter(IProgress<RunUpdate> updates, bool fetchCommunityIgnores) : IModCheckReporter
{
    /// <summary>The GUI never blocks on console input; it answers prompts from its settings.</summary>
    public bool IsInteractive
    {
        get { return true; }
    }

    private void Log(RunLogLevel level, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            updates.Report(new LogAdded(level, text.Trim()));
        }
    }

    // ----- Layout-only calls (no GUI equivalent) -----

    public void Banner() { }

    public void Rule() { }

    public void Blank() { }

    // ----- Progress and messages -----

    public void Heading(string text)
    {
        var title = text.Trim().TrimEnd('.').Trim();
        updates.Report(new StageChanged(title));
        updates.Report(new ProgressChanged(0, 0));
    }

    public void Status(string text)
    {
        Log(RunLogLevel.Info, text);
    }

    public void Success(string text)
    {
        // The CLI prints a bare "OK" after "Validating…" on the same line; on its own line it says nothing.
        if (string.Equals(text.Trim(), "OK", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Log(RunLogLevel.Success, text);
    }

    public void Warning(string text)
    {
        Log(RunLogLevel.Warning, text);
    }

    public void Error(string text)
    {
        Log(RunLogLevel.Error, text);
    }

    public void CouldNotReadModDll(string fileName, string reason)
    {
        Log(RunLogLevel.Warning, $"Could not read {fileName}: {reason}");
    }

    public void CouldNotReadSptVersion(string reason)
    {
        Log(RunLogLevel.Error, $"Could not read the SPT version: {reason}");
    }

    public void PluginsDirectoryNotFound(string path)
    {
        Log(RunLogLevel.Warning, $"BepInEx plugins folder not found: {path}");
    }

    public async Task RunForgeQueryProgressAsync(int total, Func<Action<int>, Task> work)
    {
        await RunForgeQueryProgressAsync<bool>(
            total,
            async setValue =>
            {
                await work(setValue);
                return true;
            }
        );
    }

    public async Task<T> RunForgeQueryProgressAsync<T>(int total, Func<Action<int>, Task<T>> work)
    {
        updates.Report(new ProgressChanged(0, total));
        var result = await work(value => updates.Report(new ProgressChanged(value, total)));
        updates.Report(new ProgressChanged(total, total));
        return result;
    }

    public void UsingPath(string path)
    {
        Log(RunLogLevel.Info, $"Checking {path}");
    }

    public void DirectoryDoesNotExist(string path)
    {
        Log(RunLogLevel.Error, $"Folder not found: {path}");
    }

    public void ValidatingSptVersion(string version)
    {
        Log(RunLogLevel.Info, $"Found SPT {version}. Validating with Forge…");
    }

    public void SptVersionValidated(string version)
    {
        Log(RunLogLevel.Success, $"SPT {version} validated.");
    }

    public void SptUpdateAvailable(SptVersionResult latest)
    {
        Log(RunLogLevel.Info, $"SPT {latest.Version} is available.");
    }

    public void ModPulseUpdate(ModPulseUpdateResult result, SemanticVersioning.Version sptVersion)
    {
        if (result.Status == ModPulseUpdateStatus.UpdateAvailable)
        {
            Log(RunLogLevel.Info, $"ModPulse {result.LatestVersion} is available.");
        }
    }

    public void NoModsFound()
    {
        Log(RunLogLevel.Warning, "No mods found in this install.");
    }

    public void Exception(Exception ex)
    {
        Log(RunLogLevel.Error, ex.Message);
    }

    public void RemoteIgnoresMerged(int added)
    {
        Log(
            RunLogLevel.Success,
            added > 0
                ? $"Added {added} entr{(added == 1 ? "y" : "ies")} from the community ignore list."
                : "Community ignore list: already up to date."
        );
    }

    public void RemoteIgnoresUnavailable()
    {
        Log(RunLogLevel.Warning, "Community ignore list is unavailable right now; using your local list.");
    }

    public bool PromptFetchRemoteIgnores()
    {
        return fetchCommunityIgnores;
    }

    // ----- Result displays: the GUI renders ModCheckResult instead -----

    public void VersionCompatibilityResults(List<Mod> mods, SemanticVersioning.Version sptVersion) { }

    public void LoadingWarnings(List<Mod> modsWithWarnings) { }

    public void ReconciliationResults(ModReconciliationResult result) { }

    public void MisplacedMods(MisplacedModReport report) { }

    public void UnverifiedMods(List<Mod> mods) { }

    public void DependencyResults(DependencyAnalysisResult result) { }

    public void VersionTable(List<Mod> mods) { }

    // ----- CLI end-of-run flow (the GUI has buttons for these instead) -----

    public EndOfRunChoice PromptEndOfRun(int openableUpdateCount, bool canManageIgnoredUpdates)
    {
        return EndOfRunChoice.Exit;
    }

    public IReadOnlyList<Mod> SelectUpdatesToIgnore(IReadOnlyList<Mod> candidates, ISet<int> preIgnoredApiModIds)
    {
        return [];
    }

    public void UpdatePagesOpened(int opened, int total) { }

    public bool PromptReportIgnores()
    {
        return false;
    }

    public void IgnoreReportOpened(string url, bool browserOpened, bool prefilled) { }
}
