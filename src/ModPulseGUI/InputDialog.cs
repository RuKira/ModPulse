using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModPulse.Gui;

/// <summary>A small themed dialog asking for one line of text (or a secret, masked).</summary>
public sealed class InputDialog : Window
{
    private readonly TextBox? _text;
    private readonly PasswordBox? _secret;

    internal InputDialog(string title, string message, string? initial, bool isSecret, string okText)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");

        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };

        var prompt = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        prompt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        panel.Children.Add(prompt);

        Control input;
        if (isSecret)
        {
            _secret = new PasswordBox { Password = initial ?? string.Empty, Padding = new Thickness(8, 6, 8, 6) };
            input = _secret;
        }
        else
        {
            _text = new TextBox { Text = initial ?? string.Empty, Padding = new Thickness(8, 6, 8, 6) };
            input = _text;
        }

        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = okText, IsDefault = true, MinWidth = 90 };
        ok.SetResourceReference(StyleProperty, "Button.Primary");
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) =>
        {
            input.Focus();
            _text?.SelectAll();
            Keyboard.Focus(input);
        };
    }

    /// <summary>Shows the dialog; returns the entered text, or null when cancelled.</summary>
    public static string? Ask(string title, string message, string? initial, bool isSecret = false, string okText = "OK")
    {
        var dialog = new InputDialog(title, message, initial, isSecret, okText)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        return dialog._secret?.Password ?? dialog._text?.Text ?? string.Empty;
    }
}
