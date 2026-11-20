using System.IO;
using System.Text.Json;
using ModPulse.Configuration;

namespace ModPulse.Gui.Services;

/// <summary>The GUI's remembered choices, stored in <c>%AppData%/ModPulse/gui-settings.json</c>.</summary>
public sealed class GuiSettings
{
    /// <summary>How many recent installs to remember.</summary>
    public const int MaxRecent = 8;

    /// <summary>Most recently checked installs, newest first.</summary>
    public List<string> RecentPaths { get; set; } = [];

    /// <summary>Merge the community ignore list before each check.</summary>
    public bool FetchCommunityIgnores { get; set; }

    /// <summary>The GitHub token, encrypted for the current Windows user (see <see cref="TokenProtector"/>).</summary>
    public string? GitHubTokenProtected { get; set; }

    /// <summary>Window size, so the app reopens the way it was left.</summary>
    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 800;

    public bool WindowMaximized { get; set; }

    /// <summary>Moves <paramref name="path"/> to the front of the recent list.</summary>
    public void Remember(string path)
    {
        RecentPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentPaths.Insert(0, path);
        if (RecentPaths.Count > MaxRecent)
        {
            RecentPaths.RemoveRange(MaxRecent, RecentPaths.Count - MaxRecent);
        }
    }
}

/// <summary>Loads and saves <see cref="GuiSettings"/>.</summary>
public interface IGuiSettingsStore
{
    GuiSettings Load();

    void Save(GuiSettings settings);
}

/// <summary>JSON file store. Missing or unreadable files give default settings; save errors are ignored.</summary>
public sealed class GuiSettingsStore(string? filePath = null) : IGuiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath = filePath ?? Path.Combine(IgnoredUpdateOptions.DefaultDirectory, "gui-settings.json");

    public GuiSettings Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new GuiSettings()
                : new GuiSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new GuiSettings();
        }
    }

    public void Save(GuiSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; never fail over them.
        }
    }
}
