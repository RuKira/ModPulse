namespace ModPulse.Configuration;

/// <summary>Settings for GitHub update checks of off-Forge mods.</summary>
public class GitHubOptions
{
    /// <summary>Environment variable the CLI (and GUI, when no token is saved) reads a personal access token from.</summary>
    public const string TokenEnvironmentVariable = "MODPULSE_GITHUB_TOKEN";

    /// <summary>
    /// Optional personal access token. Without one GitHub allows 60 requests an hour; with one, 5000. Never logged.
    /// </summary>
    public string? Token { get; set; } = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);

    /// <summary>The local links file: <c>%AppData%/ModPulse/github-sources.json</c>.</summary>
    public string SourcesFilePath { get; set; } = DefaultSourcesFilePath;

    /// <summary>Apply the links built into ModPulse (the community list).</summary>
    public bool UseBuiltInSources { get; set; } = true;

    /// <summary>Timeout per GitHub request, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Base URL of the GitHub API (overridable for tests).</summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>The default local links file.</summary>
    public static string DefaultSourcesFilePath
    {
        get { return Path.Combine(IgnoredUpdateOptions.DefaultDirectory, "github-sources.json"); }
    }
}
