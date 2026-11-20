using Microsoft.Extensions.DependencyInjection;
using ModPulse.Configuration;
using ModPulse.Extensions;
using ModPulse.Models;
using ModPulse.Services.Interfaces;

namespace ModPulse.Gui.Services;

/// <summary>Options for one check.</summary>
/// <param name="Refresh">Ignore cached Forge answers (the CLI's <c>--refresh</c>).</param>
/// <param name="FetchCommunityIgnores">Merge the community ignore list before checking.</param>
/// <param name="GitHubToken">Optional GitHub token for checking off-Forge mods (higher rate limit).</param>
public sealed record ModCheckRunOptions(bool Refresh, bool FetchCommunityIgnores, string? GitHubToken = null);

/// <summary>Runs a mod check.</summary>
public interface IModCheckRunner
{
    /// <summary>Checks the install at <paramref name="gamePath"/>, streaming live updates to <paramref name="updates"/>.</summary>
    Task<ModCheckResult> RunAsync(
        string gamePath,
        ModCheckRunOptions options,
        IProgress<RunUpdate> updates,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Runs the same Core pipeline as the CLI. Each check gets a fresh service container, exactly like one CLI run: its
/// own in-memory caches, the refresh option, and a reporter wired to this run's progress. The on-disk Forge cache and
/// ignore list are shared through %AppData%/ModPulse as usual.
/// </summary>
public sealed class ModCheckRunner : IModCheckRunner
{
    /// <inheritdoc />
    public async Task<ModCheckResult> RunAsync(
        string gamePath,
        ModCheckRunOptions options,
        IProgress<RunUpdate> updates,
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        services.AddModPulseServices();
        services.Configure<ForgeCacheOptions>(o => o.Refresh = options.Refresh);
        if (!string.IsNullOrWhiteSpace(options.GitHubToken))
        {
            services.Configure<GitHubOptions>(o => o.Token = options.GitHubToken);
        }
        services.AddSingleton<IModCheckReporter>(new GuiModCheckReporter(updates, options.FetchCommunityIgnores));

        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<IApplicationService>();

        // Scanning and DLL loading are synchronous in places; keep all of it off the UI thread.
        return await Task.Run(() => application.RunAsync([gamePath], cancellationToken), cancellationToken);
    }
}
