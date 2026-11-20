using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModPulse.Configuration;
using ModPulse.Models;
using ModPulse.Services;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;

namespace ModPulse.Gui.Services;

/// <summary>Links off-Forge mods to GitHub repositories from the GUI, outside a check run.</summary>
public interface IGitHubLinker
{
    /// <summary>The mod's link (the user's over a built-in one), or null.</summary>
    GitHubSource? Find(Mod mod);

    /// <summary>Saves a link to <paramref name="repo"/> (<c>owner/repo</c>) in the user's links file.</summary>
    void Link(Mod mod, string repo);

    /// <summary>Removes the user's link; false when there was none.</summary>
    bool Unlink(Mod mod);

    /// <summary>Looks up the repository's latest release, bypassing (and refreshing) the cache.</summary>
    Task<GitHubLookup> LookupAsync(string repo, string? token, CancellationToken cancellationToken = default);
}

/// <summary>The real linker: the same links file and GitHub client the check pipeline uses.</summary>
public sealed class GitHubLinker : IGitHubLinker
{
    private static readonly HttpClient Http = CreateHttpClient();

    public GitHubSource? Find(Mod mod)
    {
        return NewStore().Find(mod);
    }

    public void Link(Mod mod, string repo)
    {
        NewStore().Link(mod, repo);
    }

    public bool Unlink(Mod mod)
    {
        return NewStore().Unlink(mod);
    }

    public async Task<GitHubLookup> LookupAsync(string repo, string? token, CancellationToken cancellationToken = default)
    {
        // Refresh: a link is checked against what GitHub says now; the answer is still cached for the next check.
        var cache = new ForgeResponseCache(
            Options.Create(new ForgeCacheOptions { Refresh = true }),
            NullLogger<ForgeResponseCache>.Instance
        );
        var options = new GitHubOptions();
        if (!string.IsNullOrWhiteSpace(token))
        {
            options.Token = token;
        }

        var client = new GitHubReleaseClient(Http, cache, Options.Create(options), NullLogger<GitHubReleaseClient>.Instance);
        return await client.GetLatestAsync(repo, cancellationToken);
    }

    private static GitHubSourceStore NewStore()
    {
        // A fresh store reads the file each time, so links made by the CLI or another window are picked up.
        return new GitHubSourceStore(Options.Create(new GitHubOptions()), NullLogger<GitHubSourceStore>.Instance);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppIdentity.Name, VersionInfo.SemVer));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
