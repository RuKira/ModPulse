using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModPulse.Configuration;
using ModPulse.Models;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;

namespace ModPulse.Services;

/// <summary>
/// GitHub release lookups for off-Forge mods. Answers are cached on disk with the Forge responses (see
/// <see cref="ForgeResponseCache.LifetimeFor"/>), because unauthenticated GitHub allows only 60 requests an hour.
/// Registered via AddHttpClient (no [Injectable]).
/// </summary>
public sealed class GitHubReleaseClient(
    HttpClient httpClient,
    IForgeResponseCache cache,
    IOptions<GitHubOptions> options,
    ILogger<GitHubReleaseClient> logger
) : IGitHubReleaseClient
{
    private readonly GitHubOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<GitHubLookup> GetLatestAsync(string repo, CancellationToken cancellationToken = default)
    {
        var baseUrl = _options.ApiBaseUrl.TrimEnd('/') + "/repos/" + repo;

        var latest = await GetAsync(baseUrl + "/releases/latest", cancellationToken);
        switch (latest.Status)
        {
            case HttpStatusCode.OK:
                var release = ParseRelease(latest.Body);
                return release is null ? new GitHubLookup(GitHubLookupStatus.Failed) : new GitHubLookup(GitHubLookupStatus.Found, release);

            case HttpStatusCode.NotFound:
                // No published release (or no repository): fall back to tags.
                break;

            default:
                return Failure(latest);
        }

        var tags = await GetAsync(baseUrl + "/tags?per_page=100", cancellationToken);
        if (tags.Status == HttpStatusCode.NotFound)
        {
            return new GitHubLookup(GitHubLookupStatus.NotFound);
        }

        if (tags.Status != HttpStatusCode.OK)
        {
            return Failure(tags);
        }

        var fromTag = PickTag(tags.Body, repo);
        return fromTag is null
            ? new GitHubLookup(GitHubLookupStatus.NoReleases)
            : new GitHubLookup(GitHubLookupStatus.Found, fromTag);
    }

    private static GitHubLookup Failure(Response response)
    {
        return response.RateLimitResetUtc is not null || response.Status == HttpStatusCode.TooManyRequests
            ? new GitHubLookup(GitHubLookupStatus.RateLimited, RateLimitResetUtc: response.RateLimitResetUtc)
            : new GitHubLookup(GitHubLookupStatus.Failed);
    }

    /// <summary>Parses <c>GET /repos/{repo}/releases/latest</c>.</summary>
    internal static GitHubRelease? ParseRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = String(root, "tag_name");
            var url = String(root, "html_url");
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            DateTimeOffset? published = DateTimeOffset.TryParse(String(root, "published_at"), out var p) ? p : null;
            var assets = root.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(x => String(x, "name")).OfType<string>().ToList()
                : [];

            return new GitHubRelease(tag, String(root, "name"), String(root, "body"), url, published, assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Picks the highest version from <c>GET /repos/{repo}/tags</c>: stable versions first, pre-releases only when
    /// there's nothing else. Tags with no readable version are skipped.
    /// </summary>
    internal static GitHubRelease? PickTag(string json, string repo)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var versioned = doc.RootElement.EnumerateArray()
                .Select(t => String(t, "name"))
                .OfType<string>()
                .Select(name => (Name: name, Version: ReleaseVersion.FromTag(name)))
                .Where(t => t.Version is not null)
                .ToList();

            var best = versioned.Where(t => !t.Version!.IsPreRelease).OrderByDescending(t => t.Version).FirstOrDefault();
            if (best.Name is null)
            {
                best = versioned.OrderByDescending(t => t.Version).FirstOrDefault();
            }

            return best.Name is null
                ? null
                : new GitHubRelease(
                    best.Name,
                    null,
                    null,
                    $"https://github.com/{repo}/releases/tag/{Uri.EscapeDataString(best.Name)}",
                    null,
                    [],
                    FromTags: true
                );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? String(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private async Task<Response> GetAsync(string url, CancellationToken cancellationToken)
    {
        if (cache.TryGet(url, out var cachedStatus, out var cachedBody))
        {
            return new Response((HttpStatusCode)cachedStatus, cachedBody, null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(_options.Token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token.Trim());
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound)
            {
                cache.Set(url, (int)response.StatusCode, body);
                return new Response(response.StatusCode, body, null);
            }

            DateTimeOffset? reset = null;
            if (
                response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                && Header(response, "X-RateLimit-Remaining") == "0"
            )
            {
                reset = long.TryParse(Header(response, "X-RateLimit-Reset"), out var epoch)
                    ? DateTimeOffset.FromUnixTimeSeconds(epoch)
                    : DateTimeOffset.UtcNow.AddHours(1);
            }

            logger.LogWarning("GitHub answered {Status} for {Url}", (int)response.StatusCode, url);
            return new Response(response.StatusCode, body, reset);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "GitHub request failed for {Url}", url);
            return new Response(0, string.Empty, null);
        }
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    private sealed record Response(HttpStatusCode Status, string Body, DateTimeOffset? RateLimitResetUtc);
}
