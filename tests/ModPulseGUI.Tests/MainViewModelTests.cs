using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModPulse.Configuration;
using ModPulse.Gui.Services;
using ModPulse.Gui.ViewModels;
using ModPulse.Models;
using ModPulse.Services;

namespace ModPulse.Gui.Tests;

/// <summary>The window's logic: running a check, filling the tabs, ignoring updates, settings.</summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modpulse-gui-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeRunner _runner = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly FakeShell _shell = new();

    public MainViewModelTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private string IgnoreFile
    {
        get { return Path.Combine(_dir, "ignored-updates.json"); }
    }

    private IgnoredUpdateStore NewIgnoreStore()
    {
        return new IgnoredUpdateStore(
            Options.Create(new IgnoredUpdateOptions { FilePath = IgnoreFile, RemoteUrl = null }),
            NullLogger<IgnoredUpdateStore>.Instance
        );
    }

    private MainViewModel Create()
    {
        return new MainViewModel(_runner, _settings, _shell, NewIgnoreStore, () => new DateTime(2026, 11, 17, 21, 5, 0));
    }

    private ModCheckResult Completed(params Mod[] mods)
    {
        return new ModCheckResult
        {
            GamePath = _dir,
            SptVersion = new SemanticVersioning.Version(4, 1, 6),
            Mods = mods,
        };
    }

    [Fact]
    public void Completed_check_sorts_mods_into_tabs()
    {
        var vm = Create();

        vm.ShowResult(
            Completed(
                TestMods.WithUpdate("Needs Update"),
                TestMods.LocallyIncompatible("Too Old"),
                TestMods.Local("Mystery"),
                TestMods.UpToDate("Fine")
            )
        );

        Assert.Equal(ViewState.Results, vm.State);
        Assert.Equal(4, vm.AllMods.Count);
        Assert.Equal("Needs Update", Assert.Single(vm.Updates).Name);
        Assert.Equal("Too Old", Assert.Single(vm.Incompatible).Name);
        Assert.Equal("Mystery", Assert.Single(vm.NotOnForge).Name);
        Assert.Equal("1 update · 1 incompatible mod", vm.Headline);
        Assert.Equal("SPT 4.1.6", vm.SptVersionText);
    }

    [Fact]
    public void Clean_install_gets_an_all_good_headline()
    {
        var vm = Create();

        vm.ShowResult(Completed(TestMods.UpToDate()));

        Assert.StartsWith("All good", vm.Headline);
    }

    [Theory]
    [InlineData(ModCheckOutcome.SptNotValidated, "No SPT install found", "Check stopped", true)]
    [InlineData(ModCheckOutcome.Failed, "went wrong", "Check stopped", true)]
    [InlineData(ModCheckOutcome.Cancelled, "cancelled", "Check cancelled", false)]
    [InlineData(ModCheckOutcome.NoMods, "has no server mods", "No mods to check", false)]
    public void Early_exit_shows_the_stopped_panel(ModCheckOutcome outcome, string message, string title, bool isError)
    {
        var vm = Create();

        vm.ShowResult(new ModCheckResult { Outcome = outcome });

        Assert.Equal(ViewState.Stopped, vm.State);
        Assert.Contains(message, vm.StoppedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(title, vm.StoppedTitle);
        Assert.Equal(isError, vm.StoppedIsError);
    }

    [Fact]
    public void Ignoring_an_update_saves_it_and_moves_the_mod_out_of_updates()
    {
        var vm = Create();
        vm.ShowResult(Completed(TestMods.WithUpdate("Same Files", "1.1.0", id: 42)));
        var row = vm.Updates.Single();

        vm.IgnoreUpdateCommand.Execute(row);

        Assert.Empty(vm.Updates);
        Assert.Equal(ModStatusKind.Ignored, row.StatusKind);
        Assert.Equal(1, vm.IgnoredCount);
        var saved = Assert.Single(NewIgnoreStore().Load());
        Assert.Equal((42, "1.0.0", "1.1.0"), (saved.ApiModId, saved.LocalVersion, saved.IgnoredLatestVersion));
        Assert.Equal(IgnoreSource.User, saved.Source);
    }

    [Fact]
    public void Unignoring_restores_the_update_and_removes_the_entry()
    {
        var vm = Create();
        vm.ShowResult(Completed(TestMods.WithUpdate("Same Files", "1.1.0", id: 42)));
        var row = vm.Updates.Single();
        vm.IgnoreUpdateCommand.Execute(row);

        vm.UnignoreUpdateCommand.Execute(row);

        Assert.Same(row, Assert.Single(vm.Updates));
        Assert.Empty(NewIgnoreStore().Load());
    }

    [Fact]
    public void Community_ignore_is_explained_and_cannot_be_removed()
    {
        // WTT- Armory 2.0.5 -> 3.0.0 is in the built-in community list.
        var mod = TestMods.WithUpdate("WTT- Armory", "3.0.0", id: 2246, installed: "2.0.5");
        mod.SetUpdateSuppressed(true);
        var vm = Create();

        vm.ShowResult(Completed(mod));

        var row = vm.AllMods.Single();
        Assert.Equal(ModStatusKind.Ignored, row.StatusKind);
        Assert.Equal(IgnoreSource.BuiltIn, row.IgnoreSource);
        Assert.Contains("community list", row.Reason);
        Assert.False(row.CanUnignore);
        Assert.Empty(vm.Updates);
        Assert.Empty(vm.Incompatible);
    }

    [Fact]
    public void Removing_a_local_entry_keeps_a_built_in_one_in_force()
    {
        var mod = TestMods.WithUpdate("WTT- Armory", "3.0.0", id: 2246, installed: "2.0.5");
        var vm = Create();
        vm.ShowResult(Completed(mod));
        var row = vm.Updates.Single();
        vm.IgnoreUpdateCommand.Execute(row); // local entry duplicates the built-in one
        Assert.Equal(IgnoreSource.User, row.IgnoreSource);

        vm.UnignoreUpdateCommand.Execute(row);

        Assert.Equal(ModStatusKind.Ignored, row.StatusKind);
        Assert.Equal(IgnoreSource.BuiltIn, row.IgnoreSource);
    }

    [Fact]
    public void Ignoring_twice_does_not_duplicate_the_entry()
    {
        var vm = Create();
        vm.ShowResult(Completed(TestMods.WithUpdate(id: 42)));
        var row = vm.Updates.Single();

        vm.IgnoreUpdateCommand.Execute(row);
        vm.IgnoreUpdateCommand.Execute(row);

        Assert.Single(NewIgnoreStore().Load());
    }

    [Fact]
    public async Task Check_runs_with_the_chosen_options_and_remembers_the_install()
    {
        _runner.Result = Completed(TestMods.UpToDate());
        var vm = Create();
        vm.GamePath = _dir;
        vm.RefreshFromForge = true;
        vm.FetchCommunityIgnores = true;

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(new ModCheckRunOptions(Refresh: true, FetchCommunityIgnores: true), _runner.LastOptions);
        Assert.Equal(ViewState.Results, vm.State);
        Assert.False(vm.RefreshFromForge); // one-off
        Assert.Equal(_dir, vm.RecentPaths[0]);
        Assert.Equal(_dir, _settings.Saved!.RecentPaths[0]);
        Assert.True(_settings.Saved.FetchCommunityIgnores);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task Missing_folder_stops_without_running()
    {
        var vm = Create();
        vm.GamePath = Path.Combine(_dir, "nope");

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Null(_runner.LastOptions);
        Assert.Equal(ViewState.Stopped, vm.State);
    }

    [Fact]
    public void Live_updates_drive_stage_progress_and_log()
    {
        var vm = Create();

        vm.Apply(new StageChanged("Verifying Forge records for 3 mods"));
        vm.Apply(new ProgressChanged(0, 3));
        vm.Apply(new ProgressChanged(2, 3));
        vm.Apply(new ProgressChanged(1, 3)); // out-of-order report from a parallel request
        vm.Apply(new LogAdded(RunLogLevel.Warning, "Careful"));

        Assert.Equal("Verifying Forge records for 3 mods", vm.StageTitle);
        Assert.Equal(2, vm.ProgressValue);
        Assert.Equal("2 / 3", vm.ProgressText);
        Assert.False(vm.IsProgressIndeterminate);
        Assert.Equal("Careful", Assert.Single(vm.Activity).Text);
    }

    [Fact]
    public void Filter_matches_name_author_guid_and_status()
    {
        var vm = Create();
        vm.ShowResult(Completed(TestMods.WithUpdate("Alpha"), TestMods.UpToDate("Beta")));

        vm.ModFilter = "update";
        Assert.Equal(["Alpha"], vm.AllModsView.Cast<ModRowViewModel>().Select(r => r.Name));

        vm.ModFilter = "com.test.beta";
        Assert.Equal(["Beta"], vm.AllModsView.Cast<ModRowViewModel>().Select(r => r.Name));

        vm.ModFilter = string.Empty;
        Assert.Equal(2, vm.AllModsView.Cast<object>().Count());
    }

    [Fact]
    public void Summary_lists_the_problems_for_sharing()
    {
        var vm = Create();
        vm.ShowResult(Completed(TestMods.WithUpdate("Needs Update"), TestMods.LocallyIncompatible("Too Old")));

        var summary = vm.BuildSummary();

        Assert.Contains("SPT 4.1.6, 2 mods", summary);
        Assert.Contains("**Incompatible**", summary);
        Assert.Contains("- Too Old 1.0.0:", summary);
        Assert.Contains("- Needs Update: 1.0.0 → 1.1.0", summary);
    }

    [Fact]
    public void Opening_many_update_pages_asks_first()
    {
        var vm = Create();
        vm.ShowResult(Completed(Enumerable.Range(1, 6).Select(i => TestMods.WithUpdate($"Mod {i}", id: i)).ToArray()));
        _shell.ConfirmAnswer = false;

        vm.OpenAllUpdatePagesCommand.Execute(null);

        Assert.Equal(1, _shell.ConfirmCount);
        Assert.Empty(_shell.OpenedUrls);
    }

    // ----- Fakes -----

    private sealed class FakeRunner : IModCheckRunner
    {
        public ModCheckResult Result { get; set; } = new();

        public ModCheckRunOptions? LastOptions { get; private set; }

        public Task<ModCheckResult> RunAsync(
            string gamePath,
            ModCheckRunOptions options,
            IProgress<RunUpdate> updates,
            CancellationToken cancellationToken
        )
        {
            LastOptions = options;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSettingsStore : IGuiSettingsStore
    {
        public GuiSettings? Saved { get; private set; }

        public GuiSettings Load()
        {
            return new GuiSettings();
        }

        public void Save(GuiSettings settings)
        {
            Saved = settings;
        }
    }

    private sealed class FakeShell : IShell
    {
        public List<string> OpenedUrls { get; } = [];

        public bool ConfirmAnswer { get; set; } = true;

        public int ConfirmCount { get; private set; }

        public void OpenUrl(string url)
        {
            OpenedUrls.Add(url);
        }

        public void OpenInExplorer(string path) { }

        public string? PickFolder(string? startPath)
        {
            return null;
        }

        public void CopyText(string text) { }

        public bool Confirm(string title, string message)
        {
            ConfirmCount++;
            return ConfirmAnswer;
        }
    }
}
