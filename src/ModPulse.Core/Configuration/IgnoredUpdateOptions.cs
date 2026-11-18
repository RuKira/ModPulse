namespace ModPulse.Configuration;

/// <summary>
/// Configuration for the ignored-updates feature: where the local list is stored and where the optional
/// author-maintained remote base list is fetched from.
/// </summary>
public class IgnoredUpdateOptions
{
    /// <summary>The full path to the local ignored-updates file.</summary>
    public string FilePath { get; set; } = DefaultFilePath;

    /// <summary>
    /// URL of the author-maintained remote base list, or null/empty to disable the remote-fetch prompt entirely.
    /// </summary>
    public string? RemoteUrl { get; set; } = AppIdentity.CommunityIgnoreListUrl;

    /// <summary>
    /// Apply the community list built into ModPulse (known version mismatches). On by default; it needs no download.
    /// </summary>
    public bool UseBuiltInList { get; set; } = true;

    /// <summary>Timeout for the remote fetch, in seconds.</summary>
    public int RemoteTimeoutSeconds { get; set; } = 10;

    /// <summary>The directory holding ModPulse's app data (shared with logs and other local state).</summary>
    public static string DefaultDirectory
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.AppDataFolderName);
        }
    }

    /// <summary>The default local file path: <c>%AppData%/ModPulse/ignored-updates.json</c>.</summary>
    public static string DefaultFilePath
    {
        get { return Path.Combine(DefaultDirectory, "ignored-updates.json"); }
    }
}
