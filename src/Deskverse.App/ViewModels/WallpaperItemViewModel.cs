namespace Deskverse.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Deskverse.App.Messaging;
using Deskverse.App.Services;
using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Microsoft.UI.Dispatching;

/// <summary>
/// UI wrapper for a library wallpaper. Commands delegate to the shared
/// WallpaperActions facade so behavior stays identical across views.
/// </summary>
public partial class WallpaperItemViewModel : ObservableObject
{
    private readonly WallpaperActions _actions;
    private readonly DispatcherQueue? _dispatcher;
    private bool _thumbnailAttempted;

    public WallpaperItemViewModel(Wallpaper model, WallpaperActions actions, DispatcherQueue? dispatcher)
    {
        Model = model;
        _actions = actions;
        _dispatcher = dispatcher;
    }

    public Wallpaper Model { get; }

    public Guid Id => Model.Id;

    public string Title => string.IsNullOrWhiteSpace(Model.Title) ? "Untitled" : Model.Title;

    public string KindLabel => Model.Kind == WallpaperKind.Video ? "Video" : "Image";

    public string KindGlyph => Model.Kind == WallpaperKind.Video ? "\uE714" : "\uEB9F";

    public string DimensionsText => Model.Width > 0 && Model.Height > 0
        ? $"{Model.Width} \u00D7 {Model.Height}"
        : "Unknown size";

    public string SizeText => FormatBytes(Model.FileSizeBytes);

    public string CategoriesText => Model.Categories is { Length: > 0 }
        ? string.Join(" \u2022 ", Model.Categories)
        : "Uncategorized";

    public string? DominantColorHex => Model.DominantColor;

    public string UseCountText => Model.UseCount == 1 ? "Used once" : $"Used {Model.UseCount} times";

    public string CachedText => Model.IsCached ? "In cache" : "Not cached";

    public bool IsFavorite
    {
        get => Model.IsFavorite;
        private set
        {
            if (Model.IsFavorite == value)
            {
                return;
            }

            Model.IsFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    public bool IsPinned
    {
        get => Model.IsPinned;
        private set
        {
            if (Model.IsPinned == value)
            {
                return;
            }

            Model.IsPinned = value;
            OnPropertyChanged();
        }
    }

    public bool IsDisliked
    {
        get => Model.IsDisliked;
        private set
        {
            if (Model.IsDisliked == value)
            {
                return;
            }

            Model.IsDisliked = value;
            OnPropertyChanged();
        }
    }

    public bool IsCached
    {
        get => Model.IsCached;
        private set
        {
            if (Model.IsCached == value)
            {
                return;
            }

            Model.IsCached = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CachedText));
        }
    }

    public string FavoriteGlyph => IsFavorite ? "\uEB52" : "\uEB51";

    public bool HasThumbnail => ThumbnailPath is not null;

    [ObservableProperty]

    private string? _thumbnailPath;

    public void ApplyModel(Wallpaper model)
    {
        Model.Categories = model.Categories;
        Model.Title = model.Title;
        Model.Description = model.Description;
        Model.DominantColor = model.DominantColor;
        Model.Brightness = model.Brightness;
        Model.VisualDensity = model.VisualDensity;
        Model.IsCached = model.IsCached;
        IsFavorite = model.IsFavorite;
        IsPinned = model.IsPinned;
        IsDisliked = model.IsDisliked;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CategoriesText));
        OnPropertyChanged(nameof(DominantColorHex));
        OnPropertyChanged(nameof(DimensionsText));
    }

    public async Task LoadThumbnailAsync(IThumbnailService thumbnails)
    {
        if (_thumbnailAttempted || ThumbnailPath is not null)
        {
            return;
        }

        _thumbnailAttempted = true;
        var path = await thumbnails.GetOrCreateThumbnailAsync(Model).ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        if (_dispatcher is { HasThreadAccess: false } dispatcher)
        {
            dispatcher.TryEnqueue(() => ThumbnailPath = path);
        }
        else
        {
            ThumbnailPath = path;
        }
    }

    public void ResetThumbnail(string path)
    {
        _thumbnailAttempted = true;
        if (_dispatcher is { HasThreadAccess: false } dispatcher)
        {
            dispatcher.TryEnqueue(() => ThumbnailPath = path);
        }
        else
        {
            ThumbnailPath = path;
        }
    }

    [RelayCommand]
    private Task ApplyAsync() => _actions.ApplyAsync(Id);

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        var value = !IsFavorite;
        await _actions.SetFavoriteAsync(Id, value).ConfigureAwait(true);
        IsFavorite = value;
    }

    [RelayCommand]
    private async Task TogglePinAsync()
    {
        var value = !IsPinned;
        await _actions.SetPinnedAsync(Id, value).ConfigureAwait(true);
        IsPinned = value;
    }

    [RelayCommand]
    private async Task ToggleDislikeAsync()
    {
        var value = !IsDisliked;
        await _actions.SetDislikedAsync(Id, value).ConfigureAwait(true);
        IsDisliked = value;
    }

    [RelayCommand]
    private async Task RemoveFromCacheAsync()
    {
        await _actions.RemoveFromCacheAsync(Id).ConfigureAwait(true);
        IsCached = false;
    }

    [RelayCommand]
    private Task DeleteAsync() => _actions.DeleteAsync(Id);

    [RelayCommand]
    private void Select()
    {
        WeakReferenceMessenger.Default.Send(new WallpaperSelectedMessage(this));
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 0 => "—",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:N0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):N1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):N2} GB",
    };
}
