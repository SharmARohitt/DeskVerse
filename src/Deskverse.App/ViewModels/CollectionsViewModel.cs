namespace Deskverse.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Deskverse.App.Messaging;
using Deskverse.App.Services;
using Deskverse.Application;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Microsoft.Extensions.Logging;

/// <summary>
/// Collections page: playlists of wallpapers with create, rename, delete, and
/// membership management.
/// </summary>
public partial class CollectionsViewModel : ViewModelBase, IRecipient<LibraryChangedMessage>
{
    private readonly CollectionsService _collections;
    private readonly WallpaperActions _actions;
    private readonly IThumbnailService _thumbnails;
    private readonly NotificationService _notifications;
    private readonly ILogger<CollectionsViewModel> _logger;

    [ObservableProperty]

    private bool _isBusy;

    [ObservableProperty]

    private CollectionTileViewModel? _selectedCollection;

    [ObservableProperty]

    private string _newCollectionName= string.Empty;

    [ObservableProperty]

    private string _selectedDetailTitle= "Select a collection";

    [ObservableProperty]

    private string _selectedDetailSubtitle= "Pick one of your collections to see its wallpapers.";

    public CollectionsViewModel(
        CollectionsService collections,
        WallpaperActions actions,
        IThumbnailService thumbnails,
        NotificationService notifications,
        ILogger<CollectionsViewModel> logger)
    {
        _collections = collections;
        _actions = actions;
        _thumbnails = thumbnails;
        _notifications = notifications;
        _logger = logger;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    public ObservableCollection<CollectionTileViewModel> Collections { get; } = [];

    public ObservableCollection<WallpaperItemViewModel> SelectedItems { get; } = [];

    public bool HasNoSelectedItems => SelectedItems.Count == 0;

    public string EmptyStateText => SelectedCollection is null
        ? "Select a collection on the left."
        : "This collection has no wallpapers yet.";

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var collections = await _collections.GetAllAsync().ConfigureAwait(true);
            var tiles = collections
                .Select(c => new CollectionTileViewModel(c, this))
                .ToArray();

            await RunOnUiAsync(() =>
            {
                Collections.Clear();
                foreach (var tile in tiles)
                {
                    Collections.Add(tile);
                }

                if (SelectedCollection is null && tiles.Length > 0)
                {
                    SelectedCollection = tiles[0];
                }

                return true;
            }).ConfigureAwait(true);

            if (SelectedCollection is not null)
            {
                await LoadSelectedItemsAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Collections load failed");
            _notifications.Error("Collections could not be loaded.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var name = NewCollectionName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _notifications.Warning("Give the collection a name first.");
            return;
        }

        if (name.Length > 60)
        {
            _notifications.Warning("Collection names must be 60 characters or fewer.");
            return;
        }

        var result = await _collections.CreateAsync(name).ConfigureAwait(true);
        if (result.Success && result.Value is not null)
        {
            NewCollectionName = string.Empty;
            _notifications.Success($"Created \u201C{name}\u201D.");
            await LoadAsync().ConfigureAwait(true);
        }
        else
        {
            _notifications.Warning(result.Error ?? "The collection could not be created.");
        }
    }

    [RelayCommand]
    public async Task DeleteAsync(CollectionTileViewModel tile)
    {
        if (tile.IsSystem)
        {
            _notifications.Warning("System collections cannot be deleted.");
            return;
        }

        var deleted = await _collections.DeleteAsync(tile.Id).ConfigureAwait(true);
        if (deleted)
        {
            _notifications.Info($"Deleted \u201C{tile.Name}\u201D.");
            if (SelectedCollection?.Id == tile.Id)
            {
                SelectedCollection = null;
                SelectedItems.Clear();
                SelectedDetailTitle = "Select a collection";
                SelectedDetailSubtitle = "Pick one of your collections to see its wallpapers.";
            }

            await LoadAsync().ConfigureAwait(true);
        }
        else
        {
            _notifications.Warning("The collection could not be deleted.");
        }
    }

    [RelayCommand]
    public async Task SelectAsync(CollectionTileViewModel tile)
    {
        SelectedCollection = tile;
        await LoadSelectedItemsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedCollection is null)
        {
            return;
        }

        var name = SelectedDetailTitle.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == SelectedCollection.Name)
        {
            return;
        }

        if (name.Length > 60)
        {
            _notifications.Warning("Collection names must be 60 characters or fewer.");
            return;
        }

        try
        {
            await _collections.RenameAsync(SelectedCollection.Id, name).ConfigureAwait(true);
            SelectedCollection.Name = name;
            _notifications.Success("Collection renamed.");
        }
        catch (ArgumentException)
        {
            _notifications.Warning("That name is not valid.");
        }
    }

    public async Task AddWallpapersAsync(IReadOnlyList<Wallpaper> wallpapers)
    {
        if (SelectedCollection is null || wallpapers.Count == 0)
        {
            return;
        }

        var added = 0;
        foreach (var wallpaper in wallpapers)
        {
            var result = await _collections.AddWallpaperAsync(SelectedCollection.Id, wallpaper.Id).ConfigureAwait(true);
            if (result.Success)
            {
                added++;
            }
        }

        if (added > 0)
        {
            _notifications.Success($"Added {added} wallpaper{(added == 1 ? string.Empty : "s")} to \u201C{SelectedCollection.Name}\u201D.");
            await LoadAsync().ConfigureAwait(true);
        }
        else
        {
            _notifications.Warning("Nothing was added — the wallpapers may already be in this collection.");
        }
    }

    [RelayCommand]
    private async Task RemoveItemAsync(WallpaperItemViewModel item)
    {
        if (SelectedCollection is null)
        {
            return;
        }

        var removed = await _collections.RemoveWallpaperAsync(SelectedCollection.Id, item.Id).ConfigureAwait(true);
        if (removed)
        {
            _notifications.Info("Removed from the collection.");
            await LoadSelectedItemsAsync().ConfigureAwait(true);
        }
    }

    public WallpaperManager Manager => _actions.Manager;

    private async Task LoadSelectedItemsAsync()
    {
        if (SelectedCollection is null)
        {
            return;
        }

        var collection = await _collections.GetAsync(SelectedCollection.Id).ConfigureAwait(true);
        var items = (collection?.Items ?? [])
            .Where(i => i.Wallpaper is not null)
            .OrderByDescending(i => i.AddedAt)
            .Select(i => new WallpaperItemViewModel(i.Wallpaper!, _actions, Dispatcher))
            .ToArray();

        await RunOnUiAsync(() =>
        {
            SelectedItems.Clear();
            foreach (var item in items)
            {
                SelectedItems.Add(item);
            }

            SelectedDetailTitle = SelectedCollection.Name;
            SelectedDetailSubtitle = items.Length == 0
                ? "No wallpapers in this collection yet."
                : $"{items.Length} wallpaper{(items.Length == 1 ? string.Empty : "s")}";

            OnPropertyChanged(nameof(HasNoSelectedItems));
            OnPropertyChanged(nameof(EmptyStateText));
            return true;
        }).ConfigureAwait(true);

        foreach (var item in items)
        {
            await item.LoadThumbnailAsync(_thumbnails).ConfigureAwait(true);
        }
    }

    public void Receive(LibraryChangedMessage message) => _ = LoadAsync();
}

/// <summary>Tile summary of one collection in the left rail.</summary>
public partial class CollectionTileViewModel : ObservableObject
{
    private readonly CollectionsViewModel _owner;

    [ObservableProperty]

    private string _name;

    [ObservableProperty]

    private int _count;

    public CollectionTileViewModel(WallpaperCollection model, CollectionsViewModel owner)
    {
        Id = model.Id;
        Name = model.Name;
        Count = model.Items?.Count ?? 0;
        IsSystem = model.IsSystem;
        _owner = owner;
    }

    public Guid Id { get; }

    public bool IsSystem { get; }

    public string Glyph => IsSystem ? "\uE735" : "\uE8B7";

    public string CountText => Count == 1 ? "1 wallpaper" : $"{Count} wallpapers";

    [RelayCommand]
    private Task SelectAsync() => _owner.SelectAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(this);
}
