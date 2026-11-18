using ModPulse.Models;
using ModPulse.Services;

namespace ModPulse.Tests;

/// <summary>
/// Addons have no GUID on the Forge, so they are found through an installed parent mod. Cases are from a real 4.1.6
/// Fika install.
/// </summary>
public sealed class AddonResolverTests
{
    private static Mod Plugin(string guid, string name, string version, string path)
    {
        return new Mod
        {
            Guid = guid,
            FilePath = path,
            IsServerMod = false,
            LocalName = name,
            LocalAuthor = "Author",
            LocalVersion = version,
        };
    }

    private static Mod Parent(int id, string guid, string name, string version, string path, string? pairedPath = null)
    {
        var mod = Plugin(guid, name, version, path);
        mod.PairedComponentPath = pairedPath;
        mod.UpdateFromApiMatch(new ModSearchResult(id, null, name, name.ToLowerInvariant().Replace(' ', '-'), null, null, 0, null, $"https://sp-mod.com/mod/{id}", null, []));
        return mod;
    }

    private static AddonResult Addon(int id, string name, params (string Version, string? Constraint)[] versions)
    {
        return new AddonResult(
            id,
            name,
            name.ToLowerInvariant().Replace(' ', '-'),
            1,
            $"https://sp-mod.com/addon/{id}",
            versions.Select((v, i) => new AddonVersion(i, v.Version, $"https://dl/{id}/{v.Version}", v.Constraint)).ToList()
        );
    }

    private static Task<IReadOnlyList<AddonResult>> Addons(params AddonResult[] addons)
    {
        return Task.FromResult<IReadOnlyList<AddonResult>>(addons);
    }

    private const string Plugins = @"F:\SPT\BepInEx\plugins";

    [Fact]
    public async Task Guid_prefix_finds_the_parent_and_its_addon()
    {
        var orbit = Parent(2706, "com.chazut.orbit", "ORBIT 3.0", "3.1.1", $@"{Plugins}\ORBIT\ORBIT.dll");
        var bridge = Plugin("com.chazut.orbit.fika", "ORBIT Fika Bridge", "1.2.0", $@"{Plugins}\ORBITFika\Bridge.dll");
        AddonResult[] orbitAddons =
        [
            Addon(135, "Live-Like ORBIT - Full Config + Routes", ("1.4.2", "~3.0")),
            Addon(124, "Baseline Zones", ("2.0.0", "3.0.0")),
            Addon(123, "ORBIT - Fika Addon", ("1.2.0", "~3."), ("1.1.0", "~2.1.1")),
        ];

        var count = await AddonResolver.ResolveAsync([orbit, bridge], id => id == 2706 ? Addons(orbitAddons) : Addons());

        Assert.Equal(1, count);
        Assert.Equal(123, bridge.Addon!.Addon.Id);
        Assert.Same(orbit, bridge.Addon.Parent);
        // "~3." doesn't parse: unknown counts as fitting, never as incompatible.
        Assert.Equal(AddonStatus.UpToDate, bridge.Addon.Status);
    }

    [Fact]
    public async Task Shared_folder_finds_the_parent_through_its_paired_client()
    {
        // WTT - Black Division's client (BlackDiv.dll) sits in BlackDiv/, next to the notifier addon.
        var blackDivision = Parent(
            2511,
            "com.wtt.blackdivision",
            "WTT - Black Division",
            "1.3.1",
            @"F:\SPT\SPT_Runtime\user\mods\WTT-BlackDiv\BlackDiv.Server.dll",
            pairedPath: $@"{Plugins}\BlackDiv\BlackDiv.dll"
        );
        var notifier = Plugin("ciallo.blackdivinfo", "Black Div Notifier", "1.4.0", $@"{Plugins}\BlackDiv\BlackDivInfo.dll");

        await AddonResolver.ResolveAsync(
            [blackDivision, notifier],
            id => id == 2511 ? Addons(Addon(34, "Black Div spawn notifier", ("1.3.1", "1.3.1"), ("1.1.0", "1.2.1"))) : Addons()
        );

        Assert.Equal(34, notifier.Addon!.Addon.Id);
        Assert.Equal(AddonStatus.NewerInstalled, notifier.Addon.Status);
    }

    [Fact]
    public async Task Name_link_finds_a_parent_with_an_unrelated_guid_and_folder()
    {
        var doorRandomizer = Parent(820, "xyz.drakia.doorrandomizer", "Door Randomizer", "1.8.0", $@"{Plugins}\DrakiaXYZ-DoorRandomizer.dll");
        var sync = Plugin("com.ozen.doorrandomizer.sync", "DoorRandomizer.Sync", "1.1.0", $@"{Plugins}\ozen.DoorRandomizer.Sync\DoorRandomizer.Sync.dll");

        await AddonResolver.ResolveAsync(
            [doorRandomizer, sync],
            id => id == 820 ? Addons(Addon(3, "Door Randomizer - Fika Sync", ("1.1.0", "~1.8.0"), ("1.0.2", "~1.7.0"))) : Addons()
        );

        Assert.Equal(3, sync.Addon!.Addon.Id);
        Assert.Equal(AddonStatus.UpToDate, sync.Addon.Status);
    }

    [Fact]
    public async Task Guid_child_with_no_matching_addon_is_a_bundled_part()
    {
        var smtm = Parent(3011, "com.dark.spt.showmethemoney", "Show Me The Money", "3.0.2", $@"{Plugins}\SMTM\SMTM.dll");
        var quickSell = Plugin("com.dark.spt.showmethemoney.quicksell", "Quick Sell", "3.0.2", $@"{Plugins}\SMTM\QuickSell.dll");

        await AddonResolver.ResolveAsync([smtm, quickSell], _ => Addons());

        Assert.Null(quickSell.Addon);
        Assert.Same(smtm, quickSell.PartOf);
    }

    [Fact]
    public async Task Name_or_folder_link_alone_never_makes_a_bundled_part()
    {
        var doorRandomizer = Parent(820, "xyz.drakia.doorrandomizer", "Door Randomizer", "1.8.0", $@"{Plugins}\DoorRandomizer.dll");
        var other = Plugin("com.someone.doorrandomizerextras", "DoorRandomizer Extras", "1.0.0", $@"{Plugins}\Extras\Extras.dll");

        var count = await AddonResolver.ResolveAsync([doorRandomizer, other], _ => Addons());

        Assert.Equal(0, count);
        Assert.False(other.BelongsToAnotherMod);
    }

    [Fact]
    public void Loose_dlls_in_the_plugins_root_do_not_share_a_folder()
    {
        var parent = Parent(1, "com.a.parent", "Parent Mod", "1.0.0", $@"{Plugins}\Parent.dll");
        var loose = Plugin("com.b.thing", "Thing", "1.0.0", $@"{Plugins}\Thing.dll");

        Assert.Empty(AddonResolver.FindParents(loose, [parent]));
    }

    [Fact]
    public void Ambiguous_addon_names_pick_nothing()
    {
        var mod = Plugin("x", "Fika Sync", "1.0.0", "x.dll");

        Assert.Null(AddonResolver.PickAddon(mod, [Addon(1, "Fika Sync A"), Addon(2, "Fika Sync B")]));
    }

    [Fact]
    public void Newer_fitting_version_is_an_update()
    {
        var parent = Parent(820, "xyz.drakia.doorrandomizer", "Door Randomizer", "1.8.0", "p.dll");
        var addon = Addon(3, "Door Randomizer - Fika Sync", ("1.1.0", "~1.8.0"), ("1.0.2", "~1.7.0"));

        var match = AddonResolver.Evaluate(Plugin("g", "Sync", "1.0.2", "s.dll"), addon, parent);

        Assert.Equal(AddonStatus.IncompatibleWithParent, match.Status); // 1.0.2 needs ~1.7.0
        Assert.Equal("1.1.0", match.LatestVersion);
        Assert.Equal("https://dl/3/1.1.0", match.DownloadLink);
    }

    [Fact]
    public void Update_only_offers_versions_that_fit_the_installed_parent()
    {
        var parent = Parent(820, "xyz.drakia.doorrandomizer", "Door Randomizer", "1.7.5", "p.dll");
        var addon = Addon(3, "Door Randomizer - Fika Sync", ("1.1.0", "~1.8.0"), ("1.0.2", "~1.7.0"), ("1.0.1", "~1.7.0"));

        var match = AddonResolver.Evaluate(Plugin("g", "Sync", "1.0.1", "s.dll"), addon, parent);

        Assert.Equal(AddonStatus.UpdateAvailable, match.Status);
        Assert.Equal("1.0.2", match.LatestVersion); // 1.1.0 needs Door Randomizer 1.8
    }
}
