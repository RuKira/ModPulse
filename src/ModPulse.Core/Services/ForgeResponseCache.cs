using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModPulse.Configuration;
using ModPulse.Services.Interfaces;
using SPTarkov.DI.Annotations;

namespace ModPulse.Services;

/// <summary>
/// Stores Forge API responses on disk (one file per request URL) so repeat runs don't re-ask Forge for data that
/// hasn't changed. Each entry's lifetime depends on how quickly that kind of data goes stale; expired entries are
/// ignored and pruned. All disk errors are swallowed: the cache only ever makes things faster, never breaks a run.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class ForgeResponseCache : IForgeResponseCache
{
    private readonly ForgeCacheOptions _options;
    private readonly ILogger<ForgeResponseCache> _logger;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Creates the cache and prunes expired entries.</summary>
    public ForgeResponseCache(IOptions<ForgeCacheOptions> options, ILogger<ForgeResponseCache> logger)
        : this(options.Value, logger, () => DateTimeOffset.UtcNow) { }

    internal ForgeResponseCache(ForgeCacheOptions options, ILogger<ForgeResponseCache> logger, Func<DateTimeOffset> now)
    {
        _options = options;
        _logger = logger;
        _now = now;

        if (_options.Enabled)
        {
            PruneExpired();
        }
    }

    /// <summary>
    /// How long a response stays valid, by endpoint:
    /// <list type="bullet">
    /// <item>update status: 30 minutes, so new releases show up quickly;</item>
    /// <item>SPT version list: 6 hours;</item>
    /// <item>mod lookups and searches (including "not found"): 12 hours;</item>
    /// <item>dependencies of a specific mod version: 24 hours;</item>
    /// <item>GitHub releases of off-Forge mods: 6 hours (GitHub allows 60 unauthenticated requests an hour).</item>
    /// </list>
    /// </summary>
    public static TimeSpan LifetimeFor(string url)
    {
        if (url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromHours(6);
        }

        if (url.Contains("/mods/updates", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromMinutes(30);
        }

        if (url.Contains("/spt/versions", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromHours(6);
        }

        if (url.Contains("/mods/dependencies", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromHours(24);
        }

        return TimeSpan.FromHours(12);
    }

    /// <inheritdoc />
    public bool TryGet(string url, out int statusCode, out string body)
    {
        statusCode = 0;
        body = string.Empty;

        if (!_options.Enabled || _options.Refresh)
        {
            return false;
        }

        try
        {
            var path = PathFor(url);
            if (!File.Exists(path))
            {
                return false;
            }

            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));

            // The URL is stored to guard against (astronomically unlikely) hash collisions.
            if (entry is null || entry.Url != url || entry.ExpiresUtc <= _now())
            {
                return false;
            }

            statusCode = entry.StatusCode;
            body = entry.Body;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogDebug(ex, "Could not read cached response for {Url}", url);
            return false;
        }
    }

    /// <inheritdoc />
    public void Set(string url, int statusCode, string body)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_options.Directory);
            var path = PathFor(url);
            var entry = new Entry(url, statusCode, _now().Add(LifetimeFor(url)), body);

            // Write to a temp file and move it into place so a concurrent reader never sees a half-written entry.
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entry));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not cache response for {Url}", url);
        }
    }

    private string PathFor(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(_options.Directory, hash + ".json");
    }

    private void PruneExpired()
    {
        try
        {
            if (!Directory.Exists(_options.Directory))
            {
                return;
            }

            var now = _now();
            foreach (var file in Directory.EnumerateFiles(_options.Directory))
            {
                try
                {
                    var expired =
                        file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                        || JsonSerializer.Deserialize<Entry>(File.ReadAllText(file)) is not { } entry
                        || entry.ExpiresUtc <= now;

                    if (expired)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // Unreadable or locked: try to drop it, otherwise leave it for next time.
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception deleteEx) when (deleteEx is IOException or UnauthorizedAccessException)
                    {
                        // Best effort.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not prune the Forge response cache");
        }
    }

    private sealed record Entry(string Url, int StatusCode, DateTimeOffset ExpiresUtc, string Body);
}
