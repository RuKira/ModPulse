using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModPulse.Configuration;
using ModPulse.Models;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;

namespace ModPulse.Services;

/// <summary>
/// Checks GitHub Releases for a newer ModPulse build. Registered via AddHttpClient (no [Injectable]).
/// </summary>
public sealed class UpdateCheckService(HttpClient httpClient, ILogger<UpdateCheckService> logger) : IUpdateCheckService
{
    /// <inheritdoc />
    public async Task<ModPulseUpdateResult> CheckAsync(
        SemanticVersioning.Version sptVersion,
        CancellationToken cancellationToken = default
    )
    {
        var currentVersion = VersionInfo.SemVer;
        logger.LogDebug("Checking {Url} for ModPulse updates (current {Version})", AppIdentity.LatestReleaseApiUrl, currentVersion);

        try
        {
            using var response = await httpClient.GetAsync(AppIdentity.LatestReleaseApiUrl, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogDebug("No ModPulse release published yet");
                return new ModPulseUpdateResult(ModPulseUpdateStatus.Unavailable, currentVersion);
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return Interpret(json, currentVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogDebug(ex, "ModPulse update check failed");
            return new ModPulseUpdateResult(ModPulseUpdateStatus.Unavailable, currentVersion);
        }
    }

    /// <summary>
    /// Compares a GitHub "latest release" JSON payload against the running version.
    /// </summary>
    /// <param name="releaseJson">Body of <c>GET /repos/{owner}/{repo}/releases/latest</c>.</param>
    /// <param name="currentVersion">The running version.</param>
    public static ModPulseUpdateResult Interpret(string releaseJson, string currentVersion)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var pageUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;

        // Tags are usually "v1.2.3"; accept "1.2.3" too.
        var latest = SemVer.TryParse(tag?.TrimStart('v', 'V'));
        var current = SemVer.TryParse(currentVersion);
        if (latest is null || current is null)
        {
            return new ModPulseUpdateResult(ModPulseUpdateStatus.Unavailable, currentVersion);
        }

        return latest > current
            ? new ModPulseUpdateResult(ModPulseUpdateStatus.UpdateAvailable, currentVersion, latest.ToString(), pageUrl)
            : new ModPulseUpdateResult(ModPulseUpdateStatus.UpToDate, currentVersion, latest.ToString(), pageUrl);
    }
}
