namespace ModPulse.Configuration;

/// <summary>
/// ModPulse's own identity: name, GUID and where it is published. Change these in one place.
/// </summary>
public static class AppIdentity
{
    /// <summary>Display and product name.</summary>
    public const string Name = "ModPulse";

    /// <summary>Unique GUID for this tool (Forge listing identity). PLACEHOLDER until the final GUID is chosen.</summary>
    public const string Guid = "com.rukira.modpulse";

    /// <summary>GitHub owner used for self-update checks and issue links.</summary>
    public const string GitHubOwner = "RuKira";

    /// <summary>GitHub repository used for self-update checks and issue links.</summary>
    public const string GitHubRepo = "ModPulse";

    /// <summary>Repository URL.</summary>
    public const string RepositoryUrl = "https://github.com/" + GitHubOwner + "/" + GitHubRepo;

    /// <summary>URL for opening a new issue.</summary>
    public const string NewIssueUrl = RepositoryUrl + "/issues/new";

    /// <summary>GitHub API URL for the latest published release.</summary>
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

    /// <summary>
    /// The community ignore list (known version mismatches), served raw from the repository's main branch. Edit
    /// <c>community/ignored-updates.json</c> in the repo to update it for everyone.
    /// </summary>
    public const string CommunityIgnoreListUrl =
        "https://raw.githubusercontent.com/" + GitHubOwner + "/" + GitHubRepo + "/main/community/ignored-updates.json";

    /// <summary>Folder under %AppData% holding logs and the ignored-updates list.</summary>
    public const string AppDataFolderName = "ModPulse";
}
