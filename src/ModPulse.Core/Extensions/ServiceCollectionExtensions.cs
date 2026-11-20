using System.Net.Http.Headers;
using System.Reflection;
using ModPulse.Configuration;
using ModPulse.Logging;
using ModPulse.Services;
using ModPulse.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SPTarkov.DI;

namespace ModPulse.Extensions;

/// <summary>
/// Extension methods for configuring dependency injection services.
/// </summary>
public static class ServiceCollectionExtensions
{
    private static readonly Assembly CoreAssembly = typeof(ServiceCollectionExtensions).Assembly;

    /// <summary>
    /// Registers all ModPulse services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="frontEndAssemblies">
    /// The front end's own assemblies, scanned for <c>[Injectable]</c> types alongside Core (e.g. the CLI's
    /// Spectre reporter).
    /// </param>
    public static IServiceCollection AddModPulseServices(
        this IServiceCollection services,
        params Assembly[] frontEndAssemblies
    )
    {
        services.Configure<ForgeApiOptions>(_ => { });
        services.Configure<RateLimitOptions>(_ => { });
        services.Configure<ModScannerOptions>(_ => { });
        services.Configure<LoggingOptions>(_ => { });
        services.Configure<IgnoredUpdateOptions>(_ => { });
        services.Configure<ForgeCacheOptions>(_ => { });
        services.Configure<GitHubOptions>(_ => { });

        services.AddMemoryCache();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddFileLogger();

            // Suppress verbose HttpClient logging.
            builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        });

        var diHandler = new DependencyInjectionHandler(services);
        diHandler.AddInjectableTypesFromAssembly(CoreAssembly);
        foreach (var assembly in frontEndAssemblies.Where(a => a != CoreAssembly))
        {
            diHandler.AddInjectableTypesFromAssembly(assembly);
        }

        diHandler.InjectAll();

        // Register ForgeApiService as a typed HttpClient.
        services.AddHttpClient<IForgeApiService, ForgeApiService>(
            (serviceProvider, client) =>
            {
                var rateLimitOptions = serviceProvider.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(rateLimitOptions.RequestTimeoutSeconds);

                var version = CoreAssembly.GetName().Version?.ToString() ?? "0.0.0";
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppIdentity.Name, version));
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("(+" + AppIdentity.RepositoryUrl + ")")
                );
            }
        );

        // Register the remote ignore-list client as a typed HttpClient.
        services.AddHttpClient<IRemoteIgnoreFileClient, RemoteIgnoreFileClient>(
            (serviceProvider, client) =>
            {
                var ignoredOptions = serviceProvider.GetRequiredService<IOptions<IgnoredUpdateOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(ignoredOptions.RemoteTimeoutSeconds);

                var version = CoreAssembly.GetName().Version?.ToString() ?? "0.0.0";
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppIdentity.Name, version));
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("(+" + AppIdentity.RepositoryUrl + ")")
                );
            }
        );

        // Self-update check against GitHub Releases. GitHub's API requires a User-Agent.
        services.AddHttpClient<IUpdateCheckService, UpdateCheckService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);

            var version = CoreAssembly.GetName().Version?.ToString() ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppIdentity.Name, version));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        });

        // GitHub release checks for off-Forge mods linked to a repository.
        services.AddHttpClient<IGitHubReleaseClient, GitHubReleaseClient>(
            (serviceProvider, client) =>
            {
                var gitHubOptions = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(gitHubOptions.TimeoutSeconds);

                var version = CoreAssembly.GetName().Version?.ToString() ?? "0.0.0";
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppIdentity.Name, version));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            }
        );

        return services;
    }
}
