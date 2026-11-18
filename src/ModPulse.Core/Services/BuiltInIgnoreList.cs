using System.Text.Json;
using System.Text.Json.Serialization;
using ModPulse.Models;

namespace ModPulse.Services;

/// <summary>
/// The community ignore list (known version mismatches) as it was when ModPulse was built, embedded from
/// <c>community/ignored-updates.json</c>. It applies on every run with no download; fetching the remote list only adds
/// entries published since the build.
/// </summary>
public static class BuiltInIgnoreList
{
    private const string ResourceName = "ModPulse.Community.ignored-updates.json";

    private static readonly Lazy<IReadOnlyList<IgnoredUpdate>> _entries = new(Read);

    private static readonly Lazy<HashSet<string>> _keys = new(() =>
        Entries.Select(e => e.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)
    );

    /// <summary>The embedded entries, tagged <see cref="IgnoreSource.BuiltIn"/>.</summary>
    public static IReadOnlyList<IgnoredUpdate> Entries
    {
        get { return _entries.Value; }
    }

    /// <summary>Match keys (<see cref="IgnoredUpdate.Key"/>) of the embedded entries.</summary>
    internal static IReadOnlySet<string> Keys
    {
        get { return _keys.Value; }
    }

    private static IReadOnlyList<IgnoredUpdate> Read()
    {
        try
        {
            using var stream = typeof(BuiltInIgnoreList).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return [];
            }

            var file = JsonSerializer.Deserialize<IgnoredUpdatesFile>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() },
                }
            );

            return file?.Ignored?.Where(e => e.IsWellFormed).Select(e => e with { Source = IgnoreSource.BuiltIn }).ToList()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
