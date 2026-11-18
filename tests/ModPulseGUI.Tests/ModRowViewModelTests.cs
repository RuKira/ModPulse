using ModPulse.Gui.ViewModels;

namespace ModPulse.Gui.Tests;

/// <summary>Each Core mod state maps to one GUI status, with the most urgent winning.</summary>
public sealed class ModRowViewModelTests
{
    [Fact]
    public void Locally_incompatible_wins_and_points_at_the_compatible_download()
    {
        var row = new ModRowViewModel(TestMods.LocallyIncompatible(compatible: "2.0.0"));

        Assert.Equal(ModStatusKind.Incompatible, row.StatusKind);
        Assert.Contains("Version 2.0.0 supports this SPT", row.Reason);
        Assert.EndsWith("/2.0.0", row.DownloadUrl);
    }

    [Fact]
    public void Incompatible_without_a_compatible_version_says_so()
    {
        var row = new ModRowViewModel(TestMods.LocallyIncompatible(compatible: null));

        Assert.Contains("No version on Forge supports this SPT yet", row.Reason);
        Assert.Null(row.DownloadUrl);
    }

    [Fact]
    public void Update_shows_both_versions_and_uses_forges_download_link()
    {
        var row = new ModRowViewModel(TestMods.WithUpdate(latest: "1.1.0"));

        Assert.Equal(ModStatusKind.Update, row.StatusKind);
        Assert.Equal("1.0.0 → 1.1.0", row.Reason);
        Assert.Equal("https://dl.example/1.1.0", row.DownloadUrl);
    }

    [Fact]
    public void Suppressed_update_is_shown_as_ignored()
    {
        var mod = TestMods.WithUpdate();
        mod.SetUpdateSuppressed(true);

        Assert.Equal(ModStatusKind.Ignored, new ModRowViewModel(mod).StatusKind);
    }

    [Fact]
    public void Unmatched_mod_is_not_on_forge_and_has_no_links()
    {
        var row = new ModRowViewModel(TestMods.Local());

        Assert.Equal(ModStatusKind.NotOnForge, row.StatusKind);
        Assert.Null(row.ForgeUrl);
        Assert.Null(row.DownloadUrl);
    }

    [Fact]
    public void Up_to_date_mod_has_a_forge_page_but_no_download()
    {
        var row = new ModRowViewModel(TestMods.UpToDate());

        Assert.Equal(ModStatusKind.UpToDate, row.StatusKind);
        Assert.NotNull(row.ForgeUrl);
        Assert.Null(row.DownloadUrl);
    }

    [Fact]
    public void Paired_mod_reports_both_sides()
    {
        var mod = TestMods.Local();
        mod.PairedComponentPath = @"C:\SPT\SPT_Runtime\user\mods\Test\Test.dll";

        Assert.Equal("Server + Client", new ModRowViewModel(mod).Side);
    }

    [Fact]
    public void Addon_row_uses_the_addon_listing_and_cannot_be_ignored()
    {
        var parent = TestMods.UpToDate("ORBIT 3.0");
        var mod = TestMods.Local("ORBIT Fika Bridge", "1.1.0");
        mod.SetAddon(
            new ModPulse.Models.AddonMatch(
                new ModPulse.Models.AddonResult(123, "ORBIT - Fika Addon", "orbit-fika-addon", 2706, "https://sp-mod.com/addon/123/orbit-fika-addon", []),
                parent,
                ModPulse.Models.AddonStatus.UpdateAvailable,
                "1.2.0",
                "https://dl/123/1.2.0",
                "1.1.0 → 1.2.0"
            )
        );

        var row = new ModRowViewModel(mod);

        Assert.Equal("ORBIT - Fika Addon", row.Name);
        Assert.Equal(ModStatusKind.Update, row.StatusKind);
        Assert.Equal("Addon", row.RelationTag);
        Assert.StartsWith("Addon of ORBIT 3.0", row.RelationText);
        Assert.Equal("1.2.0", row.LatestVersion);
        Assert.Equal("https://dl/123/1.2.0", row.DownloadUrl);
        Assert.Equal("https://sp-mod.com/addon/123/orbit-fika-addon", row.ForgeUrl);
        Assert.False(row.CanIgnore);
    }

    [Fact]
    public void Bundled_part_is_neither_unknown_nor_not_on_forge()
    {
        var parent = TestMods.UpToDate("Show Me The Money");
        var mod = TestMods.Local("Quick Sell");
        mod.SetPartOf(parent);

        var row = new ModRowViewModel(mod);

        Assert.Equal(ModStatusKind.Bundled, row.StatusKind);
        Assert.Equal("Bundled", row.RelationTag);
        Assert.Equal(parent.ApiUrl, row.ForgeUrl);
    }

    [Theory]
    [InlineData("no_version_for_spt", "Forge has no version of this mod for your SPT version.")]
    [InlineData("some_other_code", "some other code.")]
    [InlineData("Already a sentence.", "Already a sentence.")]
    [InlineData(null, "Forge reports this mod is incompatible with your SPT version.")]
    public void Forge_reason_codes_become_sentences(string? code, string expected)
    {
        Assert.Equal(expected, ModRowViewModel.FriendlyForgeReason(code));
    }
}
