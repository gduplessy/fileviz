using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace FileViz.App.Views;

/// <summary>Maps a snapshot state ("Complete", "Partial · Stale", ...) to a badge kind used by the StateBadge style.</summary>
public sealed class StateKindConverter : IValueConverter
{
    public static string Kind(string? state)
    {
        if (string.IsNullOrEmpty(state))
            return "None";
        if (state.Contains("Stale", StringComparison.Ordinal) || state.StartsWith("Partial", StringComparison.Ordinal) || state.StartsWith("Interrupted", StringComparison.Ordinal))
            return "Caution";
        return state switch
        {
            "Complete" => "Ok",
            "Scanning" => "Accent",
            "Failed" => "Critical",
            _ => "Neutral"
        };
    }
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Kind(value as string);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

/// <summary>Visible when the value is false.</summary>
public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

/// <summary>Visible when the value is a non-empty string.</summary>
public sealed class TextVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

/// <summary>Boolean negation.</summary>
public sealed class NegateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Formats a byte count with binary units.</summary>
public sealed class BytesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is long bytes ? FileViz.Core.Format.Bytes(bytes) : "";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
