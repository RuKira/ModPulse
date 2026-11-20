using System.Text;
using ModPulse.Models;
using ModPulse.Services;
using ModPulse.Utils;

namespace ModPulse.Tests;

/// <summary>
/// Library DLLs (no [BepInPlugin]) identified by a GUID naming themselves, and dependencies plugins declare in their
/// DLLs ([BepInDependency]). Cases from real installs: Effects Manager Framework's EFTEffectManager.dll, and
/// GamePanelHUD requiring KmyTarkovApi without listing it on the Forge.
/// </summary>
public sealed class LibraryAndDeclaredDependencyTests
{
    [Fact]
    public void Library_is_identified_by_the_guid_that_names_it()
    {
        // EFTEffectManager.dll carries "com.eft.effectmanager.core" (the Forge GUID of Effects Manager Framework).
        string[] strings = ["com.eft.effectmanager.core", "UnityEngine.CoreModule", "com.fika.core"];

        Assert.Equal("com.eft.effectmanager.core", LibraryDllIdentity.PickGuid(strings, "EFTEffectManager"));
    }

    [Theory]
    [InlineData("com.fika.core", "EFTEffectManager")] // mentions another mod's GUID only
    [InlineData("System.Drawing.Design", "System.Drawing.Design")] // framework assembly
    [InlineData("Unity.Collections.LowLevel", "Unity.Collections")]
    [InlineData("com.a.ui", "SomeUiThing")] // name part too short to trust
    public void Strings_that_do_not_name_the_dll_are_not_a_library_identity(string guid, string dllName)
    {
        Assert.Null(LibraryDllIdentity.PickGuid([guid], dllName));
    }

    [Fact]
    public void Candidate_strings_are_read_from_utf16_and_utf8()
    {
        var bytes = Encoding.Unicode.GetBytes("xx com.eft.effectmanager.core xx")
            .Concat(Encoding.UTF8.GetBytes("\0com.author.otherthing\0"))
            .ToArray();

        var found = LibraryDllIdentity.ExtractCandidateStrings(bytes).ToList();

        Assert.Contains("com.eft.effectmanager.core", found);
        Assert.Contains("com.author.otherthing", found);
    }

    private static Mod Plugin(string guid, string name, params DeclaredDependency[] dependencies)
    {
        var mod = new Mod
        {
            Guid = guid,
            FilePath = $@"F:\SPT\BepInEx\plugins\{name}\{name}.dll",
            IsServerMod = false,
            LocalName = name,
            LocalAuthor = "Author",
            LocalVersion = "1.0.0",
        };
        mod.DeclaredDependencies.AddRange(dependencies);
        return mod;
    }

    [Fact]
    public void Missing_hard_dependency_is_reported_with_the_mods_that_need_it()
    {
        var hud = Plugin(
            "com.kmyuhkyuk.GamePanelHUDCore",
            "Game Panel HUD",
            new DeclaredDependency("com.kmyuhkyuk.KmyTarkovApi", IsHard: true)
        );

        var (guid, requiredBy) = Assert.Single(LocalDependencyChecker.FindMissing([hud], []));

        Assert.Equal("com.kmyuhkyuk.KmyTarkovApi", guid);
        Assert.Same(hud, Assert.Single(requiredBy));
    }

    [Fact]
    public void Soft_dependencies_installed_mods_alternates_and_spt_plugins_are_not_missing()
    {
        var api = Plugin("com.kmyuhkyuk.KmyTarkovApi", "Kmy Tarkov Api");
        var hud = Plugin(
            "com.kmyuhkyuk.GamePanelHUDCore",
            "Game Panel HUD",
            new DeclaredDependency("com.kmyuhkyuk.kmytarkovapi", IsHard: true), // case-insensitive
            new DeclaredDependency("com.fika.core", IsHard: false), // soft: optional
            new DeclaredDependency("com.SPT.core", IsHard: true), // shipped by SPT
            new DeclaredDependency("com.kmyuhkyuk.GamePanelHUDCompass", IsHard: true) // provided as an alternate GUID
        );
        hud.AlternateGuids.Add("com.kmyuhkyuk.GamePanelHUDCompass");

        Assert.Empty(LocalDependencyChecker.FindMissing([api, hud], ["com.SPT.core"]));
    }

    [Fact]
    public void A_dependency_needed_by_several_mods_is_listed_once()
    {
        var a = Plugin("com.x.a", "A", new DeclaredDependency("com.x.lib", true));
        var b = Plugin("com.x.b", "B", new DeclaredDependency("com.x.lib", true));

        var (_, requiredBy) = Assert.Single(LocalDependencyChecker.FindMissing([a, b], []));

        Assert.Equal(2, requiredBy.Count);
    }
}
