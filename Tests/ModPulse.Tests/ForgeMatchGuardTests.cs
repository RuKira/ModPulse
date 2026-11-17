using ModPulse.Models;
using ModPulse.Utils;

namespace ModPulse.Tests;

/// <summary>
/// The fuzzy-match guard rejects similarly named Forge listings that belong to a different author.
/// </summary>
public sealed class ForgeMatchGuardTests
{
    private static Mod Local(string guid, string author)
    {
        return new Mod
        {
            Guid = guid,
            FilePath = "mod.dll",
            IsServerMod = true,
            LocalName = "Hardcore Ruleset",
            LocalAuthor = author,
            LocalVersion = "1.0.0",
        };
    }

    private static ModSearchResult Forge(string? guid, string owner)
    {
        return new ModSearchResult(784, null, "Hardcore Rules", "hardcore-rules", null, null, 0, null, null, new ModAuthor(1, owner, null), null)
        {
            Guid = guid,
        };
    }

    [Fact]
    public void Different_guid_and_author_is_a_different_mod()
    {
        Assert.True(
            ForgeMatchGuard.IsDifferentMod(Local("com.rukira.hardcoreruleset", "RuKira"), Forge("com.danw.hardcorerules", "DanW"))
        );
    }

    [Theory]
    [InlineData("com.rukira.ruleset", "RuKira", "com.rukira.hardcorerules", "Someone")] // same GUID namespace
    [InlineData("plugin.guid", "DanW", "com.danw.hardcorerules", "DanW")] // same author name
    [InlineData("plugin.guid", "danw", "com.danw.hardcorerules", "Team")] // author == Forge GUID namespace
    [InlineData("com.danw.hardcorerules", "Other", "com.danw.hardcorerules", "Other2")] // identical GUID
    public void Related_listings_are_not_rejected(string localGuid, string author, string forgeGuid, string owner)
    {
        Assert.False(ForgeMatchGuard.IsDifferentMod(Local(localGuid, author), Forge(forgeGuid, owner)));
    }

    [Fact]
    public void Missing_forge_guid_keeps_the_original_behavior()
    {
        Assert.False(ForgeMatchGuard.IsDifferentMod(Local("com.rukira.hardcoreruleset", "RuKira"), Forge(null, "DanW")));
    }

    [Theory]
    [InlineData("com.chazut.orbit.server", "com.chazut.orbit", true)]
    [InlineData("com.chazut.orbit.client", "com.chazut.orbit", true)]
    [InlineData("com.chazut.orbit.fika", "com.chazut.orbit", false)] // separately versioned integration plugin
    [InlineData("com.chazut.orbit.server.extra", "com.chazut.orbit", false)] // more than one extra segment
    [InlineData("com.chazut.orbitplus", "com.chazut.orbit", false)] // not a segment boundary
    [InlineData("com.chazut.orbit", "com.chazut.orbit", false)] // identical GUID is the GUID lookup's job
    [InlineData("fika.ghostfenixx.svm", "fika", false)] // Forge GUID too short to be a parent
    public void IsComponentOf_matches_child_guids_only(string localGuid, string forgeGuid, bool expected)
    {
        Assert.Equal(expected, ForgeMatchGuard.IsComponentOf(Local(localGuid, "x"), Forge(forgeGuid, "y")));
    }

    [Theory]
    [InlineData("com.rukira.hardcoreruleset", "rukira")]
    [InlineData("com.Some-Author.mod", "someauthor")]
    [InlineData("Author-ModName", "")]
    public void AuthorNamespace_reads_the_second_segment(string guid, string expected)
    {
        Assert.Equal(expected, ForgeMatchGuard.AuthorNamespace(guid));
    }
}
