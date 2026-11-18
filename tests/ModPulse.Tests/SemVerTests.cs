using ModPulse.Utils;

namespace ModPulse.Tests;

/// <summary>
/// Tests for <see cref="SemVer"/>, the central version-parsing helper used in place of exception-driven parsing.
/// </summary>
public sealed class SemVerTests
{
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("0.0.1")]
    [InlineData("1.0.0-beta.1")]
    public void TryParse_returns_version_for_valid_input(string input)
    {
        Assert.NotNull(SemVer.TryParse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("abc")]
    public void TryParse_returns_null_for_missing_or_invalid_input(string? input)
    {
        Assert.Null(SemVer.TryParse(input));
    }

    [Fact]
    public void ParseOrZero_parses_valid_input()
    {
        Assert.Equal(new SemanticVersioning.Version("1.2.3"), SemVer.ParseOrZero("1.2.3"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    public void ParseOrZero_falls_back_to_zero(string? input)
    {
        Assert.Equal(new SemanticVersioning.Version(0, 0, 0), SemVer.ParseOrZero(input));
    }

    [Theory]
    [InlineData(">=1.0.0", "1.2.3", true)]
    [InlineData("~1.2.0", "1.2.5", true)]
    [InlineData("~1.2.0", "1.3.0", false)]
    [InlineData(">=2.0.0", "1.9.9", false)]
    public void SatisfiesRange_evaluates_constraint(string constraint, string version, bool expected)
    {
        Assert.Equal(expected, SemVer.SatisfiesRange(constraint, new SemanticVersioning.Version(version)));
    }

    [Theory]
    // Same minor, built for an older or equal patch: supported.
    [InlineData("4.1.5", "4.1.6", true)]
    [InlineData("4.1.3", "4.1.6", true)]
    [InlineData("4.1.6", "4.1.6", true)]
    [InlineData("~4.1.5", "4.1.6", true)]
    [InlineData("3.11.1", "3.11.4", true)]
    [InlineData("3.10.0", "3.10.5", true)]
    // Built for a newer patch than installed: not supported.
    [InlineData("4.1.6", "4.1.5", false)]
    [InlineData("~4.1.5", "4.1.3", false)]
    // Across minors or majors: never supported.
    [InlineData("~4.0 <4.1.0", "4.1.6", false)]
    [InlineData("~4.0.0", "4.1.6", false)]
    [InlineData("4.0.13", "4.1.0", false)]
    [InlineData("~3.11.0", "4.0.0", false)]
    [InlineData("3.11.4", "4.1.6", false)]
    public void SupportsSpt_accepts_older_patches_within_the_same_minor(string constraint, string spt, bool expected)
    {
        Assert.Equal(expected, SemVer.SupportsSpt(constraint, new SemanticVersioning.Version(spt)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a range")]
    public void SupportsSpt_returns_false_for_missing_or_invalid_constraint(string? constraint)
    {
        Assert.False(SemVer.SupportsSpt(constraint, new SemanticVersioning.Version("4.1.6")));
    }

    [Theory]
    [InlineData(new[] { "4.1.5" }, "4.1.6", true)]
    [InlineData(new[] { "4.0.13", "4.1.2" }, "4.1.6", true)]
    [InlineData(new[] { "4.1.7" }, "4.1.6", false)]
    [InlineData(new[] { "4.0.13" }, "4.1.6", false)]
    public void AnySupportsSpt_uses_the_same_rule_for_listed_versions(string[] listed, string spt, bool expected)
    {
        Assert.Equal(expected, SemVer.AnySupportsSpt(listed, new SemanticVersioning.Version(spt)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SatisfiesRange_returns_false_for_missing_constraint(string? constraint)
    {
        Assert.False(SemVer.SatisfiesRange(constraint, new SemanticVersioning.Version("1.0.0")));
    }
}
