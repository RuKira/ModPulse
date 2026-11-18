using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ModPulse.Gui.Services;
using ModPulse.Gui.ViewModels;

namespace ModPulse.Gui;

/// <summary>The main window. View-only code: sizing, the dark title bar, and a few focus conveniences.</summary>
public partial class MainWindow : Window
{
    private const int AllModsTab = 6;

    private readonly MainViewModel _viewModel;
    private readonly IGuiSettingsStore _settingsStore;

    public MainWindow(MainViewModel viewModel, IGuiSettingsStore settingsStore)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsStore = settingsStore;
        DataContext = viewModel;

        var settings = viewModel.Settings;
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        SourceInitialized += (_, _) => UseDarkTitleBar();
        Closing += OnClosing;

        // Keep the live log scrolled to the newest line while a check runs.
        viewModel.Activity.CollectionChanged += OnActivityChanged;

        // Ctrl+F jumps to the All mods filter.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, _) => FocusFilter()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Find, Key.F, ModifierKeys.Control));
    }

    private void OnActivityChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is not { Count: > 0 } added)
        {
            return;
        }

        // Defer until the list has processed the change; scrolling inside this handler races the list's own update.
        var newest = added[^1];
        Dispatcher.BeginInvoke(
            () =>
            {
                if (RunningLog.IsVisible && RunningLog.Items.Contains(newest))
                {
                    RunningLog.ScrollIntoView(newest);
                }
            },
            System.Windows.Threading.DispatcherPriority.Background
        );
    }

    private void FocusFilter()
    {
        if (_viewModel.State != ViewState.Results)
        {
            return;
        }

        _viewModel.SelectedTabIndex = AllModsTab;
        Dispatcher.BeginInvoke(() =>
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
        });
    }

    private void RecentButton_Click(object sender, RoutedEventArgs e)
    {
        if (RecentButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = RecentButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedMod = null;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var settings = _viewModel.Settings;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        settings.WindowWidth = bounds.Width;
        settings.WindowHeight = bounds.Height;
        _settingsStore.Save(settings);

        if (_viewModel.CheckCancelCommand.CanExecute(null))
        {
            _viewModel.CheckCancelCommand.Execute(null);
        }
    }

    // ----- Dark title bar (Windows 10 20H1+ / 11) -----

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void UseDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));

        // Windows 11 can also tint the caption to match the window background (COLORREF is 0x00BBGGRR).
        if (TryFindResource("Color.Window") is Color c)
        {
            var colorRef = c.R | (c.G << 8) | (c.B << 16);
            _ = DwmSetWindowAttribute(hwnd, DwmCaptionColor, ref colorRef, sizeof(int));
        }
    }
}
