using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModPulse.Configuration;
using ModPulse.Gui.Services;
using ModPulse.Gui.ViewModels;
using ModPulse.Models;
using ModPulse.Services;
using ModPulse.Services.Interfaces;

namespace ModPulse.Gui.Tests;

/// <summary>Off-Forge mods linked to GitHub in the GUI: statuses, the Off Forge tab, linking, ignoring and reporting.</summary>
public sealed class GitHubGuiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modpulse-gui-github", Guid.NewGuid().ToString("N"));
    private readonly Shell _shell = new();
    private readonly Linker _linker = new();

    public GitHubGuiTests()
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

    private static GitHubLookup Found(string tag)
    {
        return new GitHubLookup(
            GitHubLookupStatus.Found,
            new GitHubRelease(tag, null, null, "https://github.com/o/r/releases/tag/" + tag, null, [])
        );
    }

    private static Mod Linked(string tag, string installed = "1.0.0", GitHubSourceOrigin origin = GitHubSourceOrigin.User)
    {
        var mod = TestMods.Local("Off Mod", installed);
        mod.SetGitHub(
            GitHubUpdateResolver.Evaluate(installed, new GitHubSource(mod.Guid, "o/r", Origin: origin), Found(tag), new(4, 1, 6))
        );
        return mod;
    }

    private MainViewModel Create()
    {
        IIgnoredUpdateStore NewIgnoreStore()
        {
            return new IgnoredUpdateStore(
                Options.Create(new IgnoredUpdateOptions { FilePath = Path.Combine(_dir, "ignored.json"), RemoteUrl = null }),
                NullLogger<IgnoredUpdateStore>.Instance
            );
        }

        return new MainViewModel(new NoRunner(), new MemorySettings(), _shell, NewIgnoreStore, () => DateTime.Now, _linker);
    }

    private ModCheckResult Result(params Mod[] mods)
    {
        return new ModCheckResult
        {
            GamePath = _dir,
            SptVersion = new SemanticVersioning.Version(4, 1, 6),
            ForgeSptVersion = new SemanticVersioning.Version(4, 1, 6),
            Mods = mods,
        };
    }

    // ----- Rows -----

    [Fact]
    public void Github_update_is_an_update_with_the_release_as_download()
    {
        var row = new ModRowViewModel(Linked("v1.1.0"));

        Assert.Equal(ModStatusKind.Update, row.StatusKind);
        Assert.Equal("1.1.0", row.LatestVersion);
        Assert.Equal("https://github.com/o/r/releases/tag/v1.1.0", row.DownloadUrl);
        Assert.Equal("GitHub", row.RelationTag);
        Assert.Equal("GitHub", row.PageLabel);
        Assert.Equal("https://github.com/o/r", row.PageUrl);
        Assert.True(row.CanIgnore);
        Assert.Contains("check it supports SPT", row.Reason);
    }

    [Theory]
    [InlineData("v1.0.0", ModStatusKind.UpToDate)]
    [InlineData("v0.9.0", ModStatusKind.Newer)]
    [InlineData("nightly", ModStatusKind.NotOnForge)]
    public void Other_github_verdicts(string tag, ModStatusKind expected)
    {
        var row = new ModRowViewModel(Linked(tag));

        Assert.Equal(expected, row.StatusKind);
        Assert.True(row.IsGitHubLinked);
        Assert.Null(row.DownloadUrl);
    }

    [Fact]
    public void Unlinked_off_forge_mod_invites_a_link()
    {
        var row = new ModRowViewModel(TestMods.Local("Mystery"));

        Assert.Equal("Off Forge", row.StatusText);
        Assert.True(row.CanLinkGitHub);
        Assert.False(row.CanUnlinkGitHub);
        Assert.Equal("Link GitHub repo", row.LinkGitHubLabel);
        Assert.Null(row.RelationTag);
    }

    [Fact]
    public void Built_in_links_can_be_changed_but_not_removed()
    {
        var row = new ModRowViewModel(Linked("v1.0.0", origin: GitHubSourceOrigin.BuiltIn));

        Assert.True(row.CanLinkGitHub);
        Assert.False(row.CanUnlinkGitHub);
        Assert.Equal("Change GitHub repo", row.LinkGitHubLabel);
        Assert.Equal("o/r · community list", row.GitHubText);
    }

    // ----- Window -----

    [Fact]
    public void Off_forge_tab_lists_linked_and_unlinked_mods_and_github_updates_count_as_updates()
    {
        var vm = Create();

        vm.ShowResult(Result(Linked("v1.1.0"), TestMods.Local("Plain Off"), TestMods.UpToDate("On Forge")));

        Assert.Equal(["Off Mod", "Plain Off"], vm.NotOnForge.Select(r => r.Name));
        Assert.Equal("Off Mod", Assert.Single(vm.Updates).Name);
    }

    [Fact]
    public void Ignoring_a_github_release_then_reporting_it()
    {
        var vm = Create();
        vm.ShowResult(Result(Linked("v1.1.0")));
        var row = vm.AllMods.Single();

        vm.IgnoreUpdateCommand.Execute(row);

        Assert.Equal(ModStatusKind.Ignored, row.StatusKind);
        Assert.Empty(vm.Updates);
        Assert.True(row.CanReportMismatch);

        vm.ReportMismatchCommand.Execute(row);
        var url = Assert.Single(_shell.Opened);
        Assert.Contains("template=report-ignored-updates.yml", url);
        Assert.Contains("github", url);

        vm.UnignoreUpdateCommand.Execute(row);
        Assert.Equal(ModStatusKind.Update, row.StatusKind);
    }

    [Fact]
    public async Task Linking_checks_the_repository_right_away()
    {
        var vm = Create();
        vm.ShowResult(Result(TestMods.Local("Plain Off")));
        var row = vm.AllMods.Single();
        _shell.Answers.Enqueue("https://github.com/Some/Repo");
        _linker.Lookups["Some/Repo"] = Found("v2.0.0");

        await vm.LinkGitHubCommand.ExecuteAsync(row);

        Assert.Equal("Some/Repo", _linker.Linked[row.Mod.Guid]);
        Assert.Equal(ModStatusKind.Update, row.StatusKind);
        Assert.Equal("Some/Repo", row.GitHub!.Repo);
        Assert.Single(vm.Updates);

        await vm.UnlinkGitHubCommand.ExecuteAsync(row);

        Assert.False(_linker.Linked.ContainsKey(row.Mod.Guid));
        Assert.Equal(ModStatusKind.NotOnForge, row.StatusKind);
        Assert.Empty(vm.Updates);
    }

    [Fact]
    public async Task A_missing_repository_or_bad_input_is_not_linked()
    {
        var vm = Create();
        vm.ShowResult(Result(TestMods.Local("Plain Off")));
        var row = vm.AllMods.Single();

        _shell.Answers.Enqueue("not a repo");
        await vm.LinkGitHubCommand.ExecuteAsync(row);

        _shell.Answers.Enqueue("Gone/Repo");
        _linker.Lookups["Gone/Repo"] = new GitHubLookup(GitHubLookupStatus.NotFound);
        await vm.LinkGitHubCommand.ExecuteAsync(row);

        Assert.Empty(_linker.Linked);
        Assert.Equal(2, _shell.Informed.Count);
        Assert.False(row.IsGitHubLinked);
    }

    // ----- Fakes -----

    private sealed class Linker : IGitHubLinker
    {
        public Dictionary<string, string> Linked { get; } = [];

        public Dictionary<string, GitHubLookup> Lookups { get; } = [];

        public GitHubSource? Find(Mod mod)
        {
            return Linked.TryGetValue(mod.Guid, out var repo) ? new GitHubSource(mod.Guid, repo) : null;
        }

        public void Link(Mod mod, string repo)
        {
            Linked[mod.Guid] = repo;
        }

        public bool Unlink(Mod mod)
        {
            return Linked.Remove(mod.Guid);
        }

        public Task<GitHubLookup> LookupAsync(string repo, string? token, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Lookups.TryGetValue(repo, out var l) ? l : new GitHubLookup(GitHubLookupStatus.Failed));
        }
    }

    private sealed class Shell : IShell
    {
        public Queue<string?> Answers { get; } = new();

        public List<string> Opened { get; } = [];

        public List<string> Informed { get; } = [];

        public void OpenUrl(string url)
        {
            Opened.Add(url);
        }

        public void OpenInExplorer(string path) { }

        public string? PickFolder(string? startPath)
        {
            return null;
        }

        public void CopyText(string text) { }

        public bool Confirm(string title, string message)
        {
            return true;
        }

        public void Inform(string title, string message)
        {
            Informed.Add(message);
        }

        public string? Ask(string title, string message, string? initial = null, bool isSecret = false, string okText = "OK")
        {
            return Answers.Count > 0 ? Answers.Dequeue() : null;
        }
    }

    private sealed class MemorySettings : IGuiSettingsStore
    {
        private GuiSettings _settings = new();

        public GuiSettings Load()
        {
            return _settings;
        }

        public void Save(GuiSettings settings)
        {
            _settings = settings;
        }
    }

    private sealed class NoRunner : IModCheckRunner
    {
        public Task<ModCheckResult> RunAsync(
            string gamePath,
            ModCheckRunOptions options,
            IProgress<RunUpdate> updates,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new ModCheckResult());
        }
    }
}
