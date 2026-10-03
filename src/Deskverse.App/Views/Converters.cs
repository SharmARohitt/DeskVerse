namespace Deskverse.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

/// <summary>Static helpers for x:Bind function bindings in the views.</summary>
public static class Converters
{
    public static Visibility ToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ToVisibility(object? value) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NegateVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    public static string EyeGlyph(bool revealed) => revealed ? "Hide" : "Show";

    public static ImageSource? ToImage(string? path) => ThumbnailConverter.ToImage(path);

    public static Brush AmberWhenActive(bool value) =>
        value ? Brush("AccentAmberBrush") : Brush("TextMutedBrush");

    public static Brush DangerWhenActive(bool value) =>
        value ? Brush("DangerBrush") : Brush("TextMutedBrush");

    public static Brush TealWhenActive(bool value) =>
        value ? Brush("AccentTealBrush") : Brush("TextMutedBrush");

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public static Brush? ToBrush(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7)
        {
            return null;
        }

        try
        {
            var color = Microsoft.UI.ColorHelper.FromArgb(
                255,
                Convert.ToByte(hex.Substring(1, 2), 16),
                Convert.ToByte(hex.Substring(3, 2), 16),
                Convert.ToByte(hex.Substring(5, 2), 16));
            return new SolidColorBrush(color);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Wallpaper thumbnail file path to an ImageSource.</summary>
public sealed class ThumbnailConverter : IValueConverter
{
    public static ImageSource? ToImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return new BitmapImage(new Uri(path, UriKind.Absolute));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public object? Convert(object value, Type targetType, object parameter, string language) =>
        ToImage(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only when the bound object is non-null.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>RRGGBB (with or without '#') to a solid brush.</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        Converters.ToBrush(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
