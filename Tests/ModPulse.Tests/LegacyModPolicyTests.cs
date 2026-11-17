using ModPulse.Models;
using ModPulse.Services;
using Version = SemanticVersioning.Version;

namespace ModPulse.Tests;

/// <summary>
/// Legacy SPT (below 3.11): mods are labeled, trusted as installed, and only offered updates confirmed for the
/// installed SPT version.
/// </summary>
public sealed class LegacyModPolicyTests
{
    private static readonly Version Spt310 = new(3, 10, 5);

    private static ModVersion ApiVersion(string version, string constraint)
    {
        return new ModVersion(1, null, version, null, null, constraint, null, 0, null, null, null);
    }

    private static Mod Installed(string version = "1.0.0")
    {
        return new Mod
        {
            Guid = "com.test.mod",
            FilePath = "mod.dll",
            IsServerMod = true,
            LocalName = "Test Mod",
            LocalAuthor = "Author",
            LocalVersion = version,
        };
    }

    private static Mod WithUpdate(string latest, List<string>? sptVersions, params ModVersion[] apiVersions)
    {
        var mod = Installed();
        mod.UpdateFromApiMatch(
            new ModSearchResult(1, null, "Test Mod", "test-mod", null, null, 0, null, "https://sp-mod.com/mod/1/test-mod", null, apiVersions.ToList())
        );
        mod.UpdateFromSafeToUpdate(
            new SafeToUpdateMod(
                new ModUpdateVersion(1, 1, null, null, null, "1.0.0", null, null),
                new ModUpdateVersion(2, 1, null, null, null, latest, "https://dl", sptVersions),
                null
            )
        );
        return mod;
    }

    [Fact]
    public void Update_confirmed_for_the_installed_spt_is_kept()
    {
        var mod = WithUpdate("1.1.0", ["3.10.5", "3.10.4"]);

        var withheld = LegacyModPolicy.Apply(mod, Spt310);

        Assert.False(withheld);
        Assert.True(mod.IsLegacy);
        Assert.Equal(UpdateStatus.UpdateAvailable, mod.UpdateStatus);
        Assert.Equal("1.1.0", mod.LatestVersion);
    }

    [Fact]
    public void Update_confirmed_through_the_version_constraint_is_kept()
    {
        var mod = WithUpdate("1.1.0", null, ApiVersion("1.1.0", "~3.10.0"));

        Assert.False(LegacyModPolicy.Apply(mod, Spt310));
        Assert.Equal(UpdateStatus.UpdateAvailable, mod.UpdateStatus);
    }

    [Fact]
    public void Update_built_for_3_11_is_withheld_and_installed_version_kept()
    {
        var mod = WithUpdate("2.0.0", ["3.11.4"], ApiVersion("2.0.0", "~3.11.0"));

        var withheld = LegacyModPolicy.Apply(mod, Spt310);

        Assert.True(withheld);
        Assert.True(mod.IsLegacy);
        Assert.Equal(UpdateStatus.UpToDate, mod.UpdateStatus);
        Assert.Equal("1.0.0", mod.LatestVersion);
        Assert.Null(mod.DownloadLink);
        Assert.Equal(LegacyModPolicy.WithheldUpdateNote, mod.LegacyNote);
    }

    [Fact]
    public void Update_without_compatibility_data_is_withheld()
    {
        var mod = WithUpdate("2.0.0", null);

        Assert.True(LegacyModPolicy.Apply(mod, Spt310));
        Assert.Equal(UpdateStatus.UpToDate, mod.UpdateStatus);
    }

    [Fact]
    public void Forge_incompatible_is_treated_as_matching_the_installed_version()
    {
        var mod = Installed();
        mod.UpdateFromIncompatible(new IncompatibleMod(1, 1, null, null, "1.0.0", "No version for SPT 3.10.5", null));

        Assert.False(LegacyModPolicy.Apply(mod, Spt310));
        Assert.Equal(UpdateStatus.UpToDate, mod.UpdateStatus);
        Assert.True(mod.IsLegacy);
    }

    [Fact]
    public void ApplyAll_counts_withheld_updates_and_labels_every_mod()
    {
        List<Mod> mods = [WithUpdate("2.0.0", ["3.11.4"]), WithUpdate("1.1.0", ["3.10.5"]), Installed()];

        Assert.Equal(1, LegacyModPolicy.ApplyAll(mods, Spt310));
        Assert.All(mods, m => Assert.True(m.IsLegacy));
    }
}
