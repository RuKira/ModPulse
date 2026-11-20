using ModPulse.Services;

namespace ModPulse.Tests;

/// <summary>
/// The Forge doesn't list every SPT patch (3.11.5 isn't there). Forge queries then use the newest listed patch in the
/// same minor at or below the installed one.
/// </summary>
public sealed class SptStandInTests
{
    private static readonly string[] ForgeVersions =
    [
        "4.1.6", "4.1.5", "4.0.13", "4.0.0", "3.11.4", "3.11.3", "3.11.0", "3.10.5", "3.9.8",
    ];

    [Theory]
    [InlineData("3.11.5", "3.11.4")]
    [InlineData("3.11.9", "3.11.4")]
    [InlineData("3.11.2", "3.11.0")]
    [InlineData("4.1.7", "4.1.6")]
    [InlineData("3.10.6", "3.10.5")]
    public void Picks_the_newest_same_minor_patch_at_or_below_the_installed_one(string installed, string expected)
    {
        Assert.Equal(expected, SptInstallationService.PickStandIn(new SemanticVersioning.Version(installed), ForgeVersions)?.ToString());
    }

    [Theory]
    [InlineData("3.12.0")] // no 3.12 on the Forge
    [InlineData("4.2.0")]
    [InlineData("3.8.0")]
    public void No_stand_in_without_a_listed_patch_in_the_same_minor(string installed)
    {
        Assert.Null(SptInstallationService.PickStandIn(new SemanticVersioning.Version(installed), ForgeVersions));
    }

    [Fact]
    public void Never_picks_a_newer_patch_than_installed()
    {
        Assert.Null(SptInstallationService.PickStandIn(new SemanticVersioning.Version("4.0.0"), ["4.0.13"]));
    }
}
