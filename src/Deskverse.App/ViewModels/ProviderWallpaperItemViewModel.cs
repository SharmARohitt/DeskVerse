namespace Deskverse.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deskverse.App.Services;
using Deskverse.Core;
using Deskverse.Core.Abstractions;

/// <summary>One provider catalog entry available to download into the library.</summary>
public partial class ProviderWallpaperItemViewModel : ObservableObject
{
    private readonly DiscoverViewModel _owner;

    [ObservableProperty]
    private bool _isDownloading;

    public ProviderWallpaperItemViewModel(ProviderWallpaper model, string providerName, DiscoverViewModel owner)
    {
        Model = model;
        ProviderName = providerName;
        _owner = owner;
    }

    public ProviderWallpaper Model { get; }

    public string ProviderName { get; }

    public string Title => string.IsNullOrWhiteSpace(Model.Title) ? "Untitled" : Model.Title;

    public string KindLabel => Model.Kind == WallpaperKind.Video ? "Video" : "Image";

    public string KindGlyph => Model.Kind == WallpaperKind.Video ? "\uE714" : "\uEB9F";

    public string DimensionsText => Model.Width > 0 && Model.Height > 0
        ? $"{Model.Width} \u00D7 {Model.Height}"
        : "Unknown size";

    public string ProviderBadge => $"{ProviderName} \u2022 {KindLabel}";

    public string? LicenseText => Model.License;

    public string? CategoriesText => Model.Categories is { Length: > 0 } ? string.Join(" \u2022 ", Model.Categories) : null;

    [RelayCommand]
    private Task DownloadAsync() => _owner.DownloadItemAsync(this);
}
