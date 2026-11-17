using Microsoft.Extensions.Logging.Abstractions;
using ModPulse.Configuration;
using ModPulse.Services;

namespace ModPulse.Tests;

/// <summary>
/// The on-disk Forge response cache: round trips, expiry per endpoint, refresh/disabled modes and pruning.
/// </summary>
public sealed class ForgeResponseCacheTests : IDisposable
{
    private const string LookupUrl = "https://sp-mod.com/api/v0/mods?filter[guid]=com.chazut.orbit&include=versions";
    private const string UpdatesUrl = "https://sp-mod.com/api/v0/mods/updates?mods=1:1.0.0&spt_version=4.1.6";

    private readonly string _dir = TempWorkspace.CreateDirectory("modpulse-cache");
    private DateTimeOffset _now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        TempWorkspace.SafeDelete(_dir);
    }

    private ForgeResponseCache Create(bool enabled = true, bool refresh = false)
    {
        return new ForgeResponseCache(
            new ForgeCacheOptions { Directory = _dir, Enabled = enabled, Refresh = refresh },
            NullLogger<ForgeResponseCache>.Instance,
            () => _now
        );
    }

    [Fact]
    public void Stored_response_is_returned_on_a_later_run()
    {
        Create().Set(LookupUrl, 200, "{\"data\":[]}");

        Assert.True(Create().TryGet(LookupUrl, out var status, out var body));
        Assert.Equal(200, status);
        Assert.Equal("{\"data\":[]}", body);
    }

    [Fact]
    public void Unknown_url_is_a_miss()
    {
        Create().Set(LookupUrl, 200, "{}");

        Assert.False(Create().TryGet(LookupUrl + "&x=1", out _, out _));
    }

    [Fact]
    public void Update_status_expires_after_30_minutes_but_lookups_last_longer()
    {
        var cache = Create();
        cache.Set(UpdatesUrl, 200, "updates");
        cache.Set(LookupUrl, 200, "lookup");

        _now = _now.AddMinutes(31);

        Assert.False(cache.TryGet(UpdatesUrl, out _, out _));
        Assert.True(cache.TryGet(LookupUrl, out _, out _));
    }

    [Fact]
    public void Refresh_ignores_cached_entries_but_still_writes_new_ones()
    {
        Create().Set(LookupUrl, 200, "old");

        var refreshing = Create(refresh: true);
        Assert.False(refreshing.TryGet(LookupUrl, out _, out _));

        refreshing.Set(LookupUrl, 200, "new");
        Assert.True(Create().TryGet(LookupUrl, out _, out var body));
        Assert.Equal("new", body);
    }

    [Fact]
    public void Disabled_cache_neither_reads_nor_writes()
    {
        var cache = Create(enabled: false);
        cache.Set(LookupUrl, 200, "x");

        Assert.False(cache.TryGet(LookupUrl, out _, out _));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Expired_and_corrupt_entries_are_pruned_on_startup()
    {
        Create().Set(UpdatesUrl, 200, "updates");
        Create().Set(LookupUrl, 200, "lookup");
        File.WriteAllText(Path.Combine(_dir, "garbage.json"), "not json");

        _now = _now.AddHours(1);
        Create();

        Assert.Single(Directory.GetFiles(_dir)); // only the 12-hour lookup survives
    }

    [Theory]
    [InlineData("https://sp-mod.com/api/v0/mods/updates?mods=1:1.0.0&spt_version=4.1.6", 30)]
    [InlineData("https://sp-mod.com/api/v0/spt/versions?sort=-version&per_page=15", 6 * 60)]
    [InlineData("https://sp-mod.com/api/v0/mods/dependencies?mods=1:1.0.0", 24 * 60)]
    [InlineData("https://sp-mod.com/api/v0/mods?query=ORBIT&filter[spt_version]=4.1.6", 12 * 60)]
    public void Lifetime_depends_on_the_endpoint(string url, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), ForgeResponseCache.LifetimeFor(url));
    }
}
