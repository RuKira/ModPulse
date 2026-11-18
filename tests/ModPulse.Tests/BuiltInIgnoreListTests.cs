using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModPulse.Configuration;
using ModPulse.Models;
using ModPulse.Services;

namespace ModPulse.Tests;

/// <summary>
/// The community list is embedded in ModPulse, so known version mismatches are ignored with no download (the remote
/// list may be unreachable, e.g. a private repository).
/// </summary>
public sealed class BuiltInIgnoreListTests : IDisposable
{
    private readonly string _dir = TempWorkspace.CreateDirectory("modpulse-builtin");

    public void Dispose()
    {
        TempWorkspace.SafeDelete(_dir);
    }

    private IgnoredUpdateStore Store(bool useBuiltIn = true)
    {
        return new IgnoredUpdateStore(
            Options.Create(
                new IgnoredUpdateOptions
                {
                    FilePath = Path.Combine(_dir, "ignored-updates.json"),
                    RemoteUrl = null,
                    UseBuiltInList = useBuiltIn,
                }
            ),
            NullLogger<IgnoredUpdateStore>.Instance
        );
    }

    private static Mod ArmoryWithUpdate()
    {
        var mod = new Mod
        {
            Guid = "com.wtt.armory",
            FilePath = "WTT-Armory.dll",
            IsServerMod = true,
            LocalName = "WTT- Armory",
            LocalAuthor = "Tron",
            LocalVersion = "2.0.5",
        };
        mod.UpdateFromApiMatch(new ModSearchResult(2246, null, "WTT- Armory", "wtt-armory", null, null, 0, null, null, null, []));
        mod.UpdateFromSafeToUpdate(
            new SafeToUpdateMod(
                new ModUpdateVersion(1, 2246, null, null, null, "2.0.5", null, null),
                new ModUpdateVersion(2, 2246, null, null, null, "3.0.0", null, ["4.1.5"]),
                null
            )
        );
        return mod;
    }

    [Fact]
    public void Embedded_list_matches_the_repository_file()
    {
        Assert.Contains(BuiltInIgnoreList.Entries, e => e is { ApiModId: 2246, LocalVersion: "2.0.5", IgnoredLatestVersion: "3.0.0" });
        Assert.Contains(BuiltInIgnoreList.Entries, e => e is { ApiModId: 1089, LocalVersion: "4.0.0", IgnoredLatestVersion: "4.1.0" });
        Assert.All(BuiltInIgnoreList.Entries, e => Assert.Equal(IgnoreSource.BuiltIn, e.Source));
    }

    [Fact]
    public void Known_mismatch_is_ignored_without_any_local_entry_or_download()
    {
        var store = Store();

        Assert.Empty(store.Load());
        Assert.True(store.IsIgnored(ArmoryWithUpdate()));
        Assert.Equal(IgnoreSource.BuiltIn, store.GetIgnoreSource(ArmoryWithUpdate()));
    }

    [Fact]
    public void A_genuinely_newer_release_is_not_covered_by_the_built_in_entry()
    {
        var mod = ArmoryWithUpdate();
        mod.UpdateFromSafeToUpdate(
            new SafeToUpdateMod(
                new ModUpdateVersion(1, 2246, null, null, null, "2.0.5", null, null),
                new ModUpdateVersion(3, 2246, null, null, null, "3.0.1", null, ["4.1.6"]),
                null
            )
        );

        Assert.False(Store().IsIgnored(mod));
    }

    [Fact]
    public void Built_in_list_can_be_switched_off()
    {
        Assert.False(Store(useBuiltIn: false).IsIgnored(ArmoryWithUpdate()));
    }

    [Fact]
    public void Local_entry_wins_over_the_built_in_one_and_is_never_written_back()
    {
        var store = Store();
        store.Save([new IgnoredUpdate(2246, "2.0.5", "3.0.0")]);

        Assert.Equal(IgnoreSource.User, store.GetIgnoreSource(ArmoryWithUpdate()));
        Assert.Single(store.Load());
    }
}
