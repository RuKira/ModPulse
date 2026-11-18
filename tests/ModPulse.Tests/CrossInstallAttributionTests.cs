using ModPulse.Services;

namespace ModPulse.Tests;

/// <summary>
/// Deciding which unrelated mods in one BepInEx/plugins subfolder don't belong there.
/// </summary>
public sealed class CrossInstallAttributionTests
{
    private static (bool Ambiguous, IReadOnlyList<int> Intruders)? Attribute(string folder, params string[][] components)
    {
        return ModScannerService.FindIntruders(folder, components.Select(c => (IReadOnlyList<string>)c).ToList());
    }

    [Fact]
    public void Mod_and_its_addon_named_after_the_folder_both_belong()
    {
        // BepInEx/plugins/BlackDiv: BlackDiv.dll (com.blackdiv.tacticaltoaster) + BlackDivInfo.dll (ciallo.blackdivinfo).
        Assert.Null(Attribute("BlackDiv", ["BlackDiv"], ["BlackDivInfo"]));
    }

    [Fact]
    public void Folder_owner_keeps_its_place_and_the_unrelated_mod_is_the_intruder()
    {
        var result = Attribute("BlackDiv", ["BlackDiv"], ["SomethingElse"]);

        Assert.False(result!.Value.Ambiguous);
        Assert.Equal([1], result.Value.Intruders);
    }

    [Fact]
    public void Owner_and_addon_stay_while_an_unrelated_third_mod_is_flagged()
    {
        var result = Attribute("BlackDiv", ["BlackDivInfo"], ["Unrelated"], ["BlackDiv"]);

        Assert.Equal([1], result!.Value.Intruders);
    }

    [Fact]
    public void Without_a_name_match_the_single_largest_mod_is_the_occupant()
    {
        var result = Attribute("SomeFolder", ["A.Core", "A.Patches"], ["B"]);

        Assert.False(result!.Value.Ambiguous);
        Assert.Equal([1], result.Value.Intruders);
    }

    [Fact]
    public void Without_a_name_match_or_a_largest_mod_the_folder_is_ambiguous()
    {
        var result = Attribute("SomeFolder", ["A"], ["B"]);

        Assert.True(result!.Value.Ambiguous);
        Assert.Empty(result.Value.Intruders);
    }

    [Theory]
    [InlineData("Black-Div", "blackdiv")]
    [InlineData("blackdiv", "Black_Div.Core")]
    public void Name_matching_ignores_case_and_separators(string folder, string dll)
    {
        Assert.Null(Attribute(folder, [dll], ["BlackDivInfo"]));
    }
}
