using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModPulse.Configuration;
using ModPulse.Gui.Services;
using ModPulse.Gui.ViewModels;
using ModPulse.Services;

namespace ModPulse.Gui;

/// <summary>Starts ModPulseGUI: theme, view model and main window.</summary>
public partial class App : Application
{
    private const string LocalPalette = "pack://application:,,,/ModPulseGUI;component/Themes/Colors.local.xaml";
    private const string DefaultPalette = "pack://application:,,,/ModPulseGUI;component/Themes/Colors.default.xaml";
    private const string ControlStyles = "pack://application:,,,/ModPulseGUI;component/Themes/Controls.xaml";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The GUI keeps its own log so it can run alongside the CLI.
        LoggingOptions.LogFileName = "modpulse-gui.log";

        DispatcherUnhandledException += OnUnhandledException;

        LoadTheme();

        var settingsStore = new GuiSettingsStore();
        var viewModel = new MainViewModel(
            new ModCheckRunner(),
            settingsStore,
            new WindowsShell(),
            () =>
                new IgnoredUpdateStore(
                    Options.Create(new IgnoredUpdateOptions()),
                    NullLogger<IgnoredUpdateStore>.Instance
                )
        );

        var window = new MainWindow(viewModel, settingsStore);
        MainWindow = window;

#if DEBUG
        // Debug-only visual check: ModPulseGUI --snapshot <outDir> <gamePath> renders each screen to PNG and exits.
        if (e.Args.Length >= 3 && e.Args[0] == "--snapshot")
        {
            _ = Snapshot.RunAsync(window, viewModel, e.Args[1], e.Args[2]);
            return;
        }
#endif

        window.Show();
    }

    /// <summary>
    /// Loads the palette (the personal Colors.local.xaml when it was compiled in, otherwise the shipped default),
    /// turns every "Color.X" into a frozen "Brush.X", then adds the control styles.
    /// </summary>
    private void LoadTheme()
    {
        ResourceDictionary palette;
        try
        {
            palette = new ResourceDictionary { Source = new Uri(LocalPalette, UriKind.Absolute) };
        }
        catch (IOException)
        {
            palette = new ResourceDictionary { Source = new Uri(DefaultPalette, UriKind.Absolute) };
        }

        Resources.MergedDictionaries.Add(palette);

        foreach (var key in palette.Keys.OfType<string>().Where(k => k.StartsWith("Color.", StringComparison.Ordinal)))
        {
            if (palette[key] is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                Resources["Brush." + key["Color.".Length..]] = brush;
            }
        }

        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(ControlStyles, UriKind.Absolute) });
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the full details somewhere a bug report can point to.
        try
        {
            Directory.CreateDirectory(LoggingOptions.LogDirectory);
            File.AppendAllText(
                Path.Combine(LoggingOptions.LogDirectory, "modpulse-gui-crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}{Environment.NewLine}{Environment.NewLine}"
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }

        e.Handled = true;

        // One dialog at a time: a dialog pumps messages, and a repeating error must not stack dialogs.
        if (_showingError)
        {
            return;
        }

        _showingError = true;
        try
        {
            MessageBox.Show(
                $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were saved to {Path.Combine(LoggingOptions.LogDirectory, "modpulse-gui-crash.log")}",
                "ModPulse",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            _showingError = false;
        }
    }

    private bool _showingError;
}
