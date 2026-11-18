using ModPulse.Configuration;
using ModPulse.Extensions;
using ModPulse.Models;
using ModPulse.Services.Interfaces;
using ModPulse.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace ModPulse;

/// <summary>
/// Main entry point for the ModPulse application.
/// </summary>
public class Program
{
    private static CancellationTokenSource? _cts;
    private static bool _wasCancelled;

    /// <summary>
    /// Sets up dependency injection, runs the application, and handles any unhandled exceptions.
    /// </summary>
    /// <param name="args">
    /// Command line arguments: the SPT installation path (optional; defaults to the current folder), plus
    /// <c>--refresh</c> to ignore cached Forge responses for this run.
    /// </param>
    public static async Task Main(string[] args)
    {
        ILogger<Program>? logger = null;

        // Path reported at the end of the run; falls back to the static default if DI setup fails.
        var logFilePath = LoggingOptions.CurrentLogFilePath;

        _wasCancelled = false;
        _cts = new CancellationTokenSource();
        Console.CancelKeyPress += OnCancelKeyPress;

        ServiceProvider? serviceProvider = null;
        IIgnoredUpdateWorkflow? ignoredUpdateWorkflow = null;
        IReadOnlyList<Mod>? mods = null;

        // Flags are handled here; the remaining argument (if any) is the SPT path.
        var refreshCache = args.Any(a => string.Equals(a, "--refresh", StringComparison.OrdinalIgnoreCase));
        args = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

        try
        {
            var services = new ServiceCollection();
            services.AddModPulseServices(typeof(Program).Assembly);
            services.Configure<ForgeCacheOptions>(o => o.Refresh = refreshCache);
            serviceProvider = services.BuildServiceProvider();

            logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("ModPulse application starting. Args: {Args}", string.Join(", ", args));

            logFilePath = serviceProvider.GetRequiredService<IOptions<LoggingOptions>>().Value.LogFilePath;

            var applicationService = serviceProvider.GetRequiredService<IApplicationService>();
            ignoredUpdateWorkflow = serviceProvider.GetRequiredService<IIgnoredUpdateWorkflow>();

            mods = (await applicationService.RunAsync(args, _cts.Token)).Mods;

            logger.LogInformation("ModPulse application completed successfully");
        }
        catch (OperationCanceledException)
        {
            logger?.LogInformation("Application was cancelled by user");
        }
        catch (Exception ex)
        {
            logger?.LogCritical(ex, "Unhandled exception occurred");
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            _cts.Dispose();
            _cts = null;

            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine($"[grey]ModPulse v{VersionInfo.SemVer} (build {VersionInfo.GitHash})[/]");
            AnsiConsole.MarkupLine($"[grey]Log file: {logFilePath}[/]");

            if (!_wasCancelled && !Console.IsInputRedirected)
            {
                if (ignoredUpdateWorkflow is not null)
                {
                    await ignoredUpdateWorkflow.RunAsync(mods);
                }
                else
                {
                    // Fallback when DI setup failed before the workflow was resolved.
                    while (Console.KeyAvailable)
                    {
                        Console.ReadKey(intercept: true);
                    }

                    AnsiConsole.MarkupLine("[grey]Press any key to exit...[/]");
                    Console.ReadKey();
                }
            }

            if (serviceProvider is not null)
            {
                await serviceProvider.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Handles the Ctrl+C event to cancel the application.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The console cancel event arguments.</param>
    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _wasCancelled = true;
        _cts?.Cancel();
        AnsiConsole.MarkupLine("[yellow]Cancellation requested. Shutting down gracefully...[/]");
    }
}
