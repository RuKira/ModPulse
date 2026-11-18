using ModPulse.Models;

namespace ModPulse.Services.Interfaces;

/// <summary>
/// Interface for the main application orchestration service.
/// </summary>
public interface IApplicationService
{
    /// <summary>
    /// Main entry point for the application workflow.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// Everything the check found. <see cref="ModCheckResult.Mods"/> is the reconciled, enriched mod list (with update
    /// suppressions applied), empty on any early exit; <see cref="ModCheckResult.Outcome"/> says how the run ended.
    /// </returns>
    Task<ModCheckResult> RunAsync(string[] args, CancellationToken cancellationToken = default);
}
