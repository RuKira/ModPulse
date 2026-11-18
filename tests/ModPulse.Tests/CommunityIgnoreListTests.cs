using System.Text.Json;
using System.Text.Json.Serialization;
using ModPulse.Configuration;
using ModPulse.Models;

namespace ModPulse.Tests;

/// <summary>
/// Guards the community ignore list shipped in the repository (<c>community/ignored-updates.json</c>): it must parse
/// with the schema ModPulse reads, and every entry must be usable.
/// </summary>
public sealed class CommunityIgnoreListTests
{
    private static string FindListFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "community", "ignored-updates.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("community/ignored-updates.json not found above the test output directory");
    }

    [Fact]
    public void Shipped_list_parses_and_every_entry_is_well_formed()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        var file = JsonSerializer.Deserialize<IgnoredUpdatesFile>(File.ReadAllText(FindListFile()), options);

        Assert.NotNull(file);
        Assert.Equal(IgnoredUpdatesFile.CurrentSchemaVersion, file.SchemaVersion);
        Assert.NotEmpty(file.Ignored);
        Assert.All(file.Ignored, e => Assert.True(e.IsWellFormed, $"Malformed entry for mod {e.ApiModId}"));
        Assert.Equal(file.Ignored.Count, file.Ignored.Select(e => e.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Default_remote_url_points_at_the_shipped_list()
    {
        Assert.Equal(AppIdentity.CommunityIgnoreListUrl, new IgnoredUpdateOptions().RemoteUrl);
        Assert.EndsWith("/main/community/ignored-updates.json", AppIdentity.CommunityIgnoreListUrl);
    }
}
