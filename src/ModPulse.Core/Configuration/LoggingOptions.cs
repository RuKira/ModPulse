using Microsoft.Extensions.Logging;

namespace ModPulse.Configuration;

/// <summary>
/// Configuration options for the logging infrastructure.
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// Whether file logging is enabled. Default is true.
    /// </summary>
    public bool EnableFileLogging { get; set; } = true;

    /// <summary>
    /// The minimum log level for file logging. Default is Debug.
    /// </summary>
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// Maximum size of the log file in bytes before rotation. Default is 10 MB.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Number of log files to retain. Default is 3.
    /// </summary>
    public int RetainedFileCount { get; set; } = 3;

    /// <summary>
    /// The full path to the active log file. Defaults to <see cref="CurrentLogFilePath"/>.
    /// </summary>
    public string LogFilePath { get; set; } = CurrentLogFilePath;

    /// <summary>
    /// Gets the path to the log directory.
    /// </summary>
    public static string LogDirectory
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppIdentity.AppDataFolderName,
                "logs"
            );
        }
    }

    /// <summary>
    /// The log file's name. Each front end sets its own at startup (the CLI keeps <c>modpulse.log</c>, the GUI uses
    /// <c>modpulse-gui.log</c>) so both can run at once without writing to the same file.
    /// </summary>
    public static string LogFileName { get; set; } = "modpulse.log";

    public static string CurrentLogFilePath
    {
        get { return Path.Combine(LogDirectory, LogFileName); }
    }
}
