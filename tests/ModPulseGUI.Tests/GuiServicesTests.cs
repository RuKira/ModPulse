using System.IO;
using ModPulse.Gui.Services;
using ModPulse.Gui.ViewModels;
using ModPulse.Models;

namespace ModPulse.Gui.Tests;

/// <summary>The reporter bridge, settings and problem list.</summary>
public sealed class GuiServicesTests
{
    private sealed class Collect : IProgress<RunUpdate>
    {
        public List<RunUpdate> Updates { get; } = [];

        public void Report(RunUpdate value)
        {
            Updates.Add(value);
        }
    }

    [Fact]
    public void Headings_become_stage_titles_without_the_trailing_dots()
    {
        var sink = new Collect();
        new GuiModCheckReporter(sink, false).Heading("Verifying Forge records for 3 mods...");

        Assert.Equal("Verifying Forge records for 3 mods", Assert.IsType<StageChanged>(sink.Updates[0]).Title);
    }

    [Fact]
    public async Task Forge_progress_is_reported_from_start_to_finish()
    {
        var sink = new Collect();
        var reporter = new GuiModCheckReporter(sink, false);

        var result = await reporter.RunForgeQueryProgressAsync(
            2,
            setValue =>
            {
                setValue(1);
                return Task.FromResult(42);
            }
        );

        Assert.Equal(42, result);
        Assert.Equal(
            [new ProgressChanged(0, 2), new ProgressChanged(1, 2), new ProgressChanged(2, 2)],
            sink.Updates.OfType<ProgressChanged>()
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Community_list_prompt_is_answered_from_settings(bool setting)
    {
        Assert.Equal(setting, new GuiModCheckReporter(new Collect(), setting).PromptFetchRemoteIgnores());
    }

    [Fact]
    public void Bare_ok_is_dropped_but_real_messages_are_logged()
    {
        var sink = new Collect();
        var reporter = new GuiModCheckReporter(sink, false);

        reporter.Success("OK");
        reporter.Warning("Something odd");

        var log = Assert.IsType<LogAdded>(Assert.Single(sink.Updates));
        Assert.Equal(RunLogLevel.Warning, log.Level);
    }

    [Fact]
    public void Recent_paths_are_deduplicated_newest_first_and_capped()
    {
        var settings = new GuiSettings();
        for (var i = 0; i < GuiSettings.MaxRecent + 3; i++)
        {
            settings.Remember($@"C:\SPT{i}");
        }

        settings.Remember(@"c:\spt5");

        Assert.Equal(GuiSettings.MaxRecent, settings.RecentPaths.Count);
        Assert.Equal(@"c:\spt5", settings.RecentPaths[0]);
        Assert.Single(settings.RecentPaths, p => p.Equals(@"C:\SPT5", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Settings_round_trip_and_survive_a_corrupt_file()
    {
        var file = Path.Combine(Path.GetTempPath(), $"modpulse-gui-{Guid.NewGuid():N}.json");
        try
        {
            var store = new GuiSettingsStore(file);
            store.Save(new GuiSettings { FetchCommunityIgnores = true, RecentPaths = [@"F:\SPT"] });
            Assert.True(store.Load().FetchCommunityIgnores);

            File.WriteAllText(file, "{ not json");
            Assert.False(store.Load().FetchCommunityIgnores);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Problems_cover_misplaced_mods_load_warnings_and_pairing_notes()
    {
        var warned = TestMods.Local("Warned");
        warned.LoadWarnings.Add("Could not read metadata.");
        var server = TestMods.Local("Pair", server: true);
        var client = TestMods.Local("Pair", server: false);

        var result = new ModCheckResult
        {
            Misplaced = new MisplacedModReport(
                [new MisplacedMod(true, "com.x", "Server Thing", "1.0.0", @"C:\SPT\BepInEx\plugins\x.dll")],
                [new CrossInstalledDirectory(@"C:\SPT\BepInEx\plugins\BlackDiv", [], [], Ambiguous: true)]
            ),
            ModsWithLoadWarnings = [warned],
            Reconciliation = new ModPulse.Services.Interfaces.ModReconciliationResult
            {
                Mods = [server],
                ReconciledPairs =
                [
                    new ModPulse.Services.Interfaces.ModPair
                    {
                        ServerMod = server,
                        ClientMod = client,
                        SelectedMod = server,
                        Notes = ["GUID mismatch"],
                    },
                ],
                UnmatchedServerMods = [],
                UnmatchedClientMods = [],
            },
        };

        var problems = ProblemItem.FromResult(result);

        Assert.Equal(
            [ProblemKind.WrongFolder, ProblemKind.CrossInstalled, ProblemKind.LoadWarning, ProblemKind.PairingNote],
            problems.Select(p => p.Kind)
        );
        Assert.Equal("BlackDiv", problems[1].Title);
        Assert.Contains("whole folder was skipped", problems[1].Detail);
        Assert.Equal(2, problems.Count(p => p.IsSevere));
    }
}
