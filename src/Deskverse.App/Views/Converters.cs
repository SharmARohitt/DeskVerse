namespace Deskverse.App.Views;

using Microsoft.UI.Xaml;
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

    public static ImageSource? ToImage(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : new BitmapImage(new Uri(path, UriKind.Absolute));

    public static Brush AmberWhenActive(bool value) =>
        value ? Brush("AccentAmberBrush") : Brush("TextMutedBrush");

    public static Brush DangerWhenActive(bool value) =>
        value ? Brush("DangerBrush") : Brush("TextMutedBrush");

    public static Brush TealWhenActive(bool value) =>
        value ? Brush("AccentTealBrush") : Brush("TextMutedBrush");

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
