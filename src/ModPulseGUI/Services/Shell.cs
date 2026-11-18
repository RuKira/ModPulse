using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ModPulse.Gui.Services;

/// <summary>Operating-system actions the view models need (kept behind an interface so they can be tested).</summary>
public interface IShell
{
    /// <summary>Opens an http(s) link in the default browser.</summary>
    void OpenUrl(string url);

    /// <summary>Opens a folder in Explorer (or selects a file in it).</summary>
    void OpenInExplorer(string path);

    /// <summary>Asks the user for a folder; null when cancelled.</summary>
    string? PickFolder(string? startPath);

    /// <summary>Puts text on the clipboard.</summary>
    void CopyText(string text);

    /// <summary>Asks a yes/no question.</summary>
    bool Confirm(string title, string message);
}

/// <summary>The real Windows implementation.</summary>
public sealed class WindowsShell : IShell
{
    public void OpenUrl(string url)
    {
        if (
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        TryStart(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public void OpenInExplorer(string path)
    {
        if (File.Exists(path))
        {
            TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        else if (Directory.Exists(path))
        {
            TryStart(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    public string? PickFolder(string? startPath)
    {
        var dialog = new OpenFolderDialog { Title = "Choose your SPT folder (the game root)" };
        if (!string.IsNullOrWhiteSpace(startPath) && Directory.Exists(startPath))
        {
            dialog.InitialDirectory = startPath;
        }

        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FolderName : null;
    }

    public void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard can be briefly locked by another app; not worth an error.
        }
    }

    public bool Confirm(string title, string message)
    {
        return MessageBox.Show(
                Application.Current?.MainWindow!,
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            )
            == MessageBoxResult.Yes;
    }

    private static void TryStart(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing useful to do if the shell refuses.
        }
    }
}
