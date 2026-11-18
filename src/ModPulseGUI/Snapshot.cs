#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ModPulse.Gui.ViewModels;

namespace ModPulse.Gui;

/// <summary>
/// Debug-only: renders the window to PNG files so the layout can be checked without anyone at the screen.
/// Not compiled into release builds.
/// </summary>
internal static class Snapshot
{
    public static async Task RunAsync(MainWindow window, MainViewModel viewModel, string outDir, string gamePath)
    {
        Directory.CreateDirectory(outDir);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.Width = 1360;
        window.Height = 860;
        window.ShowActivated = false;
        window.Show();

        await Settle();
        Save(window, Path.Combine(outDir, "0-welcome.png"));

        viewModel.GamePath = gamePath;
        var run = viewModel.CheckCommand.ExecuteAsync(null);
        await Task.Delay(1500);
        await Settle();
        Save(window, Path.Combine(outDir, "1-running.png"));
        await run;

        string[] tabs = ["overview", "updates", "incompatible", "dependencies", "not-on-forge", "install-notes", "all-mods"];
        for (var i = 0; i < tabs.Length; i++)
        {
            viewModel.SelectedTabIndex = i;
            await Settle();
            Save(window, Path.Combine(outDir, $"2-{i}-{tabs[i]}.png"));
        }

        // All mods filtered to addons and bundled plugins.
        viewModel.SelectedTabIndex = 6;
        viewModel.ModFilter = "addon";
        viewModel.SelectedMod = viewModel.AllMods.FirstOrDefault(m => m.IsAddon);
        await Settle();
        Save(window, Path.Combine(outDir, "4-addons.png"));
        viewModel.ModFilter = string.Empty;

        // Details pane on the first mod of the All mods tab.
        viewModel.SelectedMod = viewModel.Incompatible.FirstOrDefault() ?? viewModel.Updates.FirstOrDefault() ?? viewModel.AllMods.FirstOrDefault();
        await Settle();
        Save(window, Path.Combine(outDir, "3-details.png"));

        File.WriteAllText(Path.Combine(outDir, "summary.md"), viewModel.BuildSummary());
        Application.Current.Shutdown();
    }

    private static async Task Settle()
    {
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(250);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Save(Window window, string path)
    {
        // The window's root visual (inside the OS frame) includes the content's margins.
        var content = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

        // Paint the window background first: the content itself is transparent.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
        }

        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
#endif
