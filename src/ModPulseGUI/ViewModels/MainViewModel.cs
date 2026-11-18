using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModPulse.Configuration;
using ModPulse.Gui.Services;
using ModPulse.Models;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;

namespace ModPulse.Gui.ViewModels;

/// <summary>Which big panel the window shows.</summary>
public enum ViewState
{
    Welcome,
    Running,
    Results,
    Stopped,
}

/// <summary>The whole window: install picker, run, results and actions.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IModCheckRunner _runner;
    private readonly IGuiSettingsStore _settingsStore;
    private readonly IShell _shell;
    private readonly Func<IIgnoredUpdateStore> _ignoreStoreFactory;
    private readonly GuiSettings _settings;
    private readonly Func<DateTime> _now;

    public MainViewModel(
        IModCheckRunner runner,
        IGuiSettingsStore settingsStore,
        IShell shell,
        Func<IIgnoredUpdateStore> ignoreStoreFactory,
        Func<DateTime>? now = null
    )
    {
        _runner = runner;
        _settingsStore = settingsStore;
        _shell = shell;
        _ignoreStoreFactory = ignoreStoreFactory;
        _now = now ?? (() => DateTime.Now);
        _settings = settingsStore.Load();

        RecentPaths = new ObservableCollection<string>(_settings.RecentPaths);
        _fetchCommunityIgnores = _settings.FetchCommunityIgnores;
        _gamePath = DetectStartPath() ?? _settings.RecentPaths.FirstOrDefault() ?? string.Empty;

        AllModsView = CollectionViewSource.GetDefaultView(AllMods);
        AllModsView.Filter = FilterMod;
        AllModsView.SortDescriptions.Add(new SortDescription(nameof(ModRowViewModel.StatusKind), ListSortDirection.Ascending));
        AllModsView.SortDescriptions.Add(new SortDescription(nameof(ModRowViewModel.Name), ListSortDirection.Ascending));
    }

    public GuiSettings Settings
    {
        get { return _settings; }
    }

    public string AppVersionText
    {
        get { return $"ModPulseGUI v{VersionInfo.SemVer} ({VersionInfo.GitHash})"; }
    }

    // ===== Install picker =====

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    private string _gamePath;

    public ObservableCollection<string> RecentPaths { get; }

    [ObservableProperty]
    private bool _refreshFromForge;

    [ObservableProperty]
    private bool _fetchCommunityIgnores;

    partial void OnFetchCommunityIgnoresChanged(bool value)
    {
        _settings.FetchCommunityIgnores = value;
        _settingsStore.Save(_settings);
    }

    // ===== Run state =====

    [ObservableProperty]
    private ViewState _state = ViewState.Welcome;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _stageTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate), nameof(ProgressText))]
    private int _progressValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate), nameof(ProgressText))]
    private int _progressTotal;

    public bool IsProgressIndeterminate
    {
        get { return ProgressTotal <= 0; }
    }

    public string ProgressText
    {
        get { return ProgressTotal > 0 ? $"{ProgressValue} / {ProgressTotal}" : string.Empty; }
    }

    /// <summary>Messages from the current or last run, newest last.</summary>
    public ObservableCollection<LogEntry> Activity { get; } = [];

    /// <summary>Why the last run stopped early (shown instead of results).</summary>
    [ObservableProperty]
    private string _stoppedMessage = string.Empty;

    // ===== Install summary =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstallInfo))]
    private string _sptVersionText = string.Empty;

    [ObservableProperty]
    private string _serverFolderText = string.Empty;

    [ObservableProperty]
    private bool _isLegacy;

    [ObservableProperty]
    private string? _sptUpdateText;

    [ObservableProperty]
    private string? _sptUpdateUrl;

    [ObservableProperty]
    private string? _modPulseUpdateText;

    [ObservableProperty]
    private string? _modPulseUpdateUrl;

    [ObservableProperty]
    private string _lastCheckedText = string.Empty;

    public bool HasInstallInfo
    {
        get { return !string.IsNullOrEmpty(SptVersionText); }
    }

    // ===== Results =====

    public ObservableCollection<ModRowViewModel> AllMods { get; } = [];

    public ICollectionView AllModsView { get; }

    public ObservableCollection<ModRowViewModel> Updates { get; } = [];

    public ObservableCollection<ModRowViewModel> Incompatible { get; } = [];

    public ObservableCollection<ModRowViewModel> NotOnForge { get; } = [];

    public ObservableCollection<ProblemItem> Problems { get; } = [];

    public ObservableCollection<MissingDependencyItem> MissingDependencies { get; } = [];

    public ObservableCollection<DependencyConflictItem> DependencyConflicts { get; } = [];

    public ObservableCollection<DependencyNodeItem> DependencyTree { get; } = [];

    [ObservableProperty]
    private int _ignoredCount;

    public int DependencyIssueCount
    {
        get { return MissingDependencies.Count + DependencyConflicts.Count; }
    }

    public int UpToDateCount
    {
        get { return AllMods.Count(m => m.StatusKind is ModStatusKind.UpToDate or ModStatusKind.Newer or ModStatusKind.Ignored); }
    }

    /// <summary>One-line verdict for the overview.</summary>
    public string Headline
    {
        get
        {
            var issues = Incompatible.Count + DependencyIssueCount + Problems.Count(p => p.IsSevere);
            if (issues == 0 && Updates.Count == 0)
            {
                return "All good. Everything is up to date and compatible.";
            }

            var parts = new List<string>();
            if (Updates.Count > 0)
            {
                parts.Add(Plural(Updates.Count, "update"));
            }

            if (Incompatible.Count > 0)
            {
                parts.Add(Plural(Incompatible.Count, "incompatible mod"));
            }

            if (DependencyIssueCount > 0)
            {
                parts.Add(Plural(DependencyIssueCount, "dependency issue"));
            }

            var severe = Problems.Count(p => p.IsSevere);
            if (severe > 0)
            {
                parts.Add(Plural(severe, "misplaced mod"));
            }

            return string.Join(" · ", parts);
        }
    }

    [ObservableProperty]
    private ModRowViewModel? _selectedMod;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _modFilter = string.Empty;

    partial void OnModFilterChanged(string value)
    {
        AllModsView.Refresh();
    }

    // ===== Commands: run =====

    private bool CanCheck()
    {
        return !IsRunning && !string.IsNullOrWhiteSpace(GamePath);
    }

    [RelayCommand(CanExecute = nameof(CanCheck), IncludeCancelCommand = true)]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        var path = GamePath.Trim().Trim('"');
        if (!Directory.Exists(path))
        {
            State = ViewState.Stopped;
            StoppedMessage = $"Folder not found: {path}";
            return;
        }

        IsRunning = true;
        State = ViewState.Running;
        Activity.Clear();
        StageTitle = "Starting";
        ProgressValue = 0;
        ProgressTotal = 0;
        SelectedMod = null;

        var updates = new Progress<RunUpdate>(Apply);
        ModCheckResult result;
        try
        {
            result = await _runner.RunAsync(
                path,
                new ModCheckRunOptions(RefreshFromForge, FetchCommunityIgnores),
                updates,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            result = new ModCheckResult { Outcome = ModCheckOutcome.Cancelled };
        }
        finally
        {
            IsRunning = false;
        }

        ShowResult(result);

        if (result.GamePath is { } checkedPath)
        {
            GamePath = checkedPath;
            _settings.Remember(checkedPath);
            _settingsStore.Save(_settings);
            RecentPaths.Clear();
            foreach (var recent in _settings.RecentPaths)
            {
                RecentPaths.Add(recent);
            }
        }

        // A refresh is a one-off; the next check can use the cache again.
        RefreshFromForge = false;
    }

    /// <summary>Applies one live update from the running check.</summary>
    internal void Apply(RunUpdate update)
    {
        switch (update)
        {
            case StageChanged stage:
                StageTitle = stage.Title;
                break;
            case ProgressChanged progress:
                ProgressTotal = progress.Total;
                ProgressValue = progress.Total > 0 ? Math.Max(ProgressValue, progress.Value) : 0;
                if (progress.Value == 0)
                {
                    ProgressValue = 0;
                }

                break;
            case LogAdded log:
                Activity.Add(new LogEntry(_now(), log.Level, log.Text));
                break;
        }
    }

    /// <summary>Fills every panel from a finished (or stopped) check.</summary>
    internal void ShowResult(ModCheckResult result)
    {
        SptVersionText = result.SptVersion is { } v ? $"SPT {v}" : string.Empty;
        ServerFolderText = result.Installation is { } install
            ? (string.Equals(install.ServerRoot, install.GameRoot, StringComparison.OrdinalIgnoreCase)
                ? "Server: game folder"
                : $"Server: {Path.GetFileName(install.ServerRoot)}")
            : string.Empty;
        IsLegacy = result.IsLegacy;
        SptUpdateText = result.SptUpdate is { } spt ? $"SPT {spt.Version} available" : null;
        SptUpdateUrl = result.SptUpdate?.Link;
        ModPulseUpdateText = result.ModPulseUpdate is { Status: ModPulseUpdateStatus.UpdateAvailable } mp
            ? $"ModPulse {mp.LatestVersion} available"
            : null;
        ModPulseUpdateUrl = result.ModPulseUpdate?.DownloadLink ?? AppIdentity.RepositoryUrl + "/releases/latest";

        AllMods.Clear();
        var ignoreStore = result.Mods.Any(m => m.UpdateSuppressed) ? _ignoreStoreFactory() : null;
        foreach (var mod in result.Mods)
        {
            var row = new ModRowViewModel(mod);
            if (ignoreStore is not null && mod.UpdateSuppressed)
            {
                row.IgnoreSource = ignoreStore.GetIgnoreSource(mod);
                row.Refresh();
            }

            AllMods.Add(row);
        }

        Problems.Clear();
        foreach (var problem in ProblemItem.FromResult(result))
        {
            Problems.Add(problem);
        }

        MissingDependencies.Clear();
        DependencyConflicts.Clear();
        DependencyTree.Clear();
        if (result.Dependencies is { } deps)
        {
            foreach (var missing in deps.MissingDependencies)
            {
                MissingDependencies.Add(MissingDependencyItem.From(missing));
            }

            foreach (var conflict in deps.Conflicts)
            {
                DependencyConflicts.Add(new DependencyConflictItem(conflict.ModName, conflict.Description));
            }

            foreach (var node in DependencyNodeItem.FromResult(deps))
            {
                DependencyTree.Add(node);
            }
        }

        RebuildStatusLists();

        switch (result.Outcome)
        {
            case ModCheckOutcome.Completed:
                State = ViewState.Results;
                LastCheckedText = $"Checked {_now():HH:mm}";
                SelectedTabIndex = 0;
                break;
            case ModCheckOutcome.NoMods when result.Misplaced is { Any: true }:
                State = ViewState.Results;
                LastCheckedText = $"Checked {_now():HH:mm}";
                SelectedTabIndex = 0;
                break;
            default:
                State = ViewState.Stopped;
                StoppedMessage = StoppedText(result);
                break;
        }
    }

    private static string StoppedText(ModCheckResult result)
    {
        return result.Outcome switch
        {
            ModCheckOutcome.InvalidPath => "That folder can't be checked. Pick your SPT game folder.",
            ModCheckOutcome.SptNotValidated =>
                "No SPT install found here, or its version couldn't be read. Pick the folder that contains EscapeFromTarkov.exe (or the SPT server folder).",
            ModCheckOutcome.NoMods => "No mods were found in this install.",
            ModCheckOutcome.Cancelled => "Check cancelled.",
            ModCheckOutcome.Failed => $"Something went wrong: {result.Error?.Message}. The log has details.",
            _ => string.Empty,
        };
    }

    /// <summary>Re-sorts mods into the per-status tabs (after a run, or after ignoring an update).</summary>
    private void RebuildStatusLists()
    {
        Fill(Updates, AllMods.Where(m => m.StatusKind is ModStatusKind.Update or ModStatusKind.Blocked));
        Fill(Incompatible, AllMods.Where(m => m.StatusKind == ModStatusKind.Incompatible));
        Fill(NotOnForge, AllMods.Where(m => m.StatusKind == ModStatusKind.NotOnForge));
        IgnoredCount = AllMods.Count(m => m.StatusKind == ModStatusKind.Ignored);

        AllModsView.Refresh();
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(UpToDateCount));
        OnPropertyChanged(nameof(DependencyIssueCount));
        OpenAllUpdatePagesCommand.NotifyCanExecuteChanged();
    }

    private static void Fill(ObservableCollection<ModRowViewModel> target, IEnumerable<ModRowViewModel> rows)
    {
        target.Clear();
        foreach (var row in rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            target.Add(row);
        }
    }

    private bool FilterMod(object item)
    {
        if (string.IsNullOrWhiteSpace(ModFilter) || item is not ModRowViewModel row)
        {
            return true;
        }

        var filter = ModFilter.Trim();
        return row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Author.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Guid.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.StatusText.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (row.RelationText?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>Opens a result tab by index (used by the overview tiles).</summary>
    [RelayCommand]
    private void SelectTab(string? index)
    {
        if (int.TryParse(index, out var tab))
        {
            SelectedTabIndex = tab;
        }
    }

    // ===== Commands: install picker =====

    [RelayCommand]
    private void Browse()
    {
        var picked = _shell.PickFolder(GamePath);
        if (picked is not null)
        {
            GamePath = picked;
        }
    }

    [RelayCommand]
    private void UseRecent(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            GamePath = path;
        }
    }

    // ===== Commands: links and files =====

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            _shell.OpenUrl(url);
        }
    }

    [RelayCommand]
    private void ShowInExplorer(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            _shell.OpenInExplorer(path);
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        _shell.OpenInExplorer(LoggingOptions.CurrentLogFilePath);
    }

    private bool CanOpenAllUpdatePages()
    {
        return Updates.Any(u => u.StatusKind == ModStatusKind.Update);
    }

    [RelayCommand(CanExecute = nameof(CanOpenAllUpdatePages))]
    private void OpenAllUpdatePages()
    {
        var targets = Updates.Where(u => u.StatusKind == ModStatusKind.Update).ToList();
        if (targets.Count > 5 && !_shell.Confirm("Open update pages", $"Open {targets.Count} browser tabs?"))
        {
            return;
        }

        foreach (var row in targets)
        {
            if (row.ForgeUrl is { } url)
            {
                _shell.OpenUrl(url);
            }
        }
    }

    // ===== Commands: ignore list =====

    /// <summary>
    /// Marks an update as a false positive: Forge's newer version has the same files as the installed one (the DLL
    /// just reports an old version). Same store and format as the CLI's "Manage ignored updates".
    /// </summary>
    [RelayCommand]
    private void IgnoreUpdate(ModRowViewModel? row)
    {
        if (row?.Mod is not { ApiModId: { } modId, LatestVersion: { } latest } mod)
        {
            return;
        }

        var store = _ignoreStoreFactory();
        var entries = store.Load().ToList();
        var entry = new IgnoredUpdate(
            modId,
            mod.LocalVersion,
            latest,
            mod.DisplayName,
            mod.Guid,
            IgnoreSource.User,
            DateTimeOffset.UtcNow
        );

        if (!entries.Any(e => string.Equals(e.Key, entry.Key, StringComparison.OrdinalIgnoreCase)))
        {
            entries.Add(entry);
            store.Save(entries);
        }

        mod.SetUpdateSuppressed(true);
        row.IgnoreSource = store.GetIgnoreSource(mod);
        row.Refresh();
        RebuildStatusLists();
    }

    [RelayCommand]
    private void UnignoreUpdate(ModRowViewModel? row)
    {
        if (row?.Mod is not { ApiModId: { } modId, LatestVersion: { } latest } mod)
        {
            return;
        }

        var store = _ignoreStoreFactory();
        var key = new IgnoredUpdate(modId, mod.LocalVersion, latest).Key;
        var entries = store.Load().ToList();
        if (entries.RemoveAll(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            store.Save(entries);
        }

        // A built-in community entry still applies after the local one is removed.
        row.IgnoreSource = store.GetIgnoreSource(mod);
        mod.SetUpdateSuppressed(row.IgnoreSource is not null);
        row.Refresh();
        RebuildStatusLists();
    }

    // ===== Commands: maintenance =====

    [RelayCommand]
    private void ClearCache()
    {
        if (!_shell.Confirm("Clear Forge cache", "Delete cached Forge answers? The next check will ask Forge for everything again (slower)."))
        {
            return;
        }

        try
        {
            if (Directory.Exists(ForgeCacheOptions.DefaultDirectory))
            {
                Directory.Delete(ForgeCacheOptions.DefaultDirectory, recursive: true);
            }

            Activity.Add(new LogEntry(_now(), RunLogLevel.Success, "Forge cache cleared."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Activity.Add(new LogEntry(_now(), RunLogLevel.Warning, $"Couldn't clear the whole cache: {ex.Message}"));
        }
    }

    /// <summary>Copies a Markdown summary (for Discord or a bug report) to the clipboard.</summary>
    [RelayCommand]
    private void CopySummary()
    {
        _shell.CopyText(BuildSummary());
        Activity.Add(new LogEntry(_now(), RunLogLevel.Success, "Summary copied to the clipboard."));
    }

    internal string BuildSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**ModPulse check**: {SptVersionText}{(IsLegacy ? " (Legacy)" : string.Empty)}, {AllMods.Count} mods");
        sb.AppendLine(Headline);

        void Section(string title, IEnumerable<string> lines)
        {
            var list = lines.ToList();
            if (list.Count == 0)
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine($"**{title}**");
            foreach (var line in list)
            {
                sb.AppendLine($"- {line}");
            }
        }

        Section("Incompatible", Incompatible.Select(m => $"{m.Name} {m.InstalledVersion}: {m.Reason}"));
        Section("Updates", Updates.Select(m => $"{m.Name}: {m.InstalledVersion} → {m.LatestVersion}"));
        Section("Missing dependencies", MissingDependencies.Select(d => $"{d.Name} {d.Version}"));
        Section("Dependency conflicts", DependencyConflicts.Select(c => $"{c.ModName}: {c.Description}"));
        Section("Install problems", Problems.Where(p => p.IsSevere).Select(p => $"{p.Title}: {p.Detail}"));
        Section("Not on Forge", NotOnForge.Select(m => $"{m.Name} {m.InstalledVersion}"));

        sb.AppendLine();
        sb.Append($"_{AppVersionText}_");
        return sb.ToString();
    }

    // ===== Helpers =====

    /// <summary>When the exe sits inside an SPT install (like the CLI), start there.</summary>
    private static string? DetectStartPath()
    {
        try
        {
            var exeDir = AppContext.BaseDirectory;
            return SptLayout.Resolve(exeDir)?.GameRoot;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Plural(int count, string noun)
    {
        return count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }
}
