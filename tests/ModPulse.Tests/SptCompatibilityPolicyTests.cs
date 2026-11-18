using ModPulse.Models;
using ModPulse.Services;
using Version = SemanticVersioning.Version;

namespace ModPulse.Tests;

/// <summary>
/// Forge's exact-version "incompatible" answers are re-evaluated with the same-minor patch rule: a build for 4.1.5 is
/// supported on 4.1.6, but a 4.0 build is not.
/// </summary>
public sealed class SptCompatibilityPolicyTests
{
    private static readonly Version Spt416 = new(4, 1, 6);

    private static ModVersion ApiVersion(string version, string constraint)
    {
        return new ModVersion(1, null, version, null, null, constraint, null, 0, null, null, null);
    }

    private static Mod ForgeSaysIncompatible(string installed, params ModVersion[] versions)
    {
        var mod = new Mod
        {
            Guid = "com.test.mod",
            FilePath = "mod.dll",
            IsServerMod = true,
            LocalName = "Test Mod",
            LocalAuthor = "Author",
            LocalVersion = installed,
        };
        mod.UpdateFromApiMatch(
            new ModSearchResult(7, null, "Test Mod", "test-mod", null, null, 0, null, "https://sp-mod.com/mod/7/test-mod", null, versions.ToList())
        );
        mod.UpdateFromIncompatible(new IncompatibleMod(7, 1, null, null, installed, "no_version_for_spt", null));
        return mod;
    }

    [Fact]
    public void Installed_build_for_an_older_patch_is_up_to_date()
    {
        // Headshot Damage Redirection on Fika2: 1.9.0 is built for exactly 4.1.5.
        var mod = ForgeSaysIncompatible("1.9.0", ApiVersion("1.9.0", "4.1.5"), ApiVersion("1.8.0", "~4.1"));

        Assert.True(SptCompatibilityPolicy.Resolve(mod, Spt416));
        Assert.Equal(UpdateStatus.UpToDate, mod.UpdateStatus);
        Assert.Equal("1.9.0", mod.LatestVersion);
        Assert.Null(mod.IncompatibilityReason);
    }

    [Fact]
    public void Newer_build_for_an_older_patch_becomes_an_update()
    {
        var mod = ForgeSaysIncompatible("1.3.12", ApiVersion("1.3.13", "4.1.5"), ApiVersion("1.3.12", "4.1.4"));

        Assert.True(SptCompatibilityPolicy.Resolve(mod, Spt416));
        Assert.Equal(UpdateStatus.UpdateAvailable, mod.UpdateStatus);
        Assert.Equal("1.3.13", mod.LatestVersion);
        Assert.EndsWith("/mod/download/7/test-mod/1.3.13", mod.DownloadLink);
    }

    [Fact]
    public void Builds_for_another_minor_stay_incompatible()
    {
        var mod = ForgeSaysIncompatible("2.0.5", ApiVersion("2.0.5", "~4.0 <4.1.0"), ApiVersion("2.1.1", "~4.0.13"));

        Assert.False(SptCompatibilityPolicy.Resolve(mod, Spt416));
        Assert.Equal(UpdateStatus.Incompatible, mod.UpdateStatus);
    }

    [Fact]
    public void Builds_for_a_newer_patch_than_installed_stay_incompatible()
    {
        var mod = ForgeSaysIncompatible("1.0.0", ApiVersion("1.0.0", "4.1.7"));

        Assert.False(SptCompatibilityPolicy.Resolve(mod, Spt416));
    }

    [Fact]
    public void Only_incompatible_mods_are_touched()
    {
        var mod = ForgeSaysIncompatible("1.0.0", ApiVersion("1.0.0", "4.1.5"));
        mod.UpdateFromUpToDate(new UpToDateMod(7, 1, null, null, "1.0.0", null));

        Assert.False(SptCompatibilityPolicy.Resolve(mod, Spt416));
        Assert.Equal(UpdateStatus.UpToDate, mod.UpdateStatus);
    }
}
