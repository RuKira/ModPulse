namespace ModPulse.Configuration;

/// <summary>
/// Settings for the on-disk Forge response cache, which lets repeat runs skip most Forge requests (and Forge's rate
/// limit).
/// </summary>
public class ForgeCacheOptions
{
    /// <summary>Whether responses are read from and written to the disk cache.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true (the <c>--refresh</c> flag), cached responses are ignored for this run; fresh responses are still
    /// written back so the next run benefits.
    /// </summary>
    public bool Refresh { get; set; }

    /// <summary>Folder holding cached responses.</summary>
    public string Directory { get; set; } = DefaultDirectory;

    /// <summary>The default cache folder: <c>%AppData%/ModPulse/cache/forge</c>.</summary>
    public static string DefaultDirectory
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppIdentity.AppDataFolderName,
                "cache",
                "forge"
            );
        }
    }
}
