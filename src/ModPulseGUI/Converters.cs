using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ModPulse.Gui;

/// <summary>Visible when the bound value's text equals the parameter (e.g. an enum value name).</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Visible when the value has something in it: true, a non-empty string, a non-zero number, a non-empty collection, or
/// any other non-null object. Pass "invert" as the parameter to flip it.
/// </summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            ICollection c => c.Count > 0,
            _ => true,
        };

        if (string.Equals(parameter?.ToString(), "invert", StringComparison.OrdinalIgnoreCase))
        {
            has = !has;
        }

        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
