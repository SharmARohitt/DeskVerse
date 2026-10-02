namespace Deskverse.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Deskverse.App.Messaging;
using Deskverse.App.Services;
using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// My Wallpapers: the local library with filters, import, and grid actions.
/// </summary>
public partial class LibraryViewModel : ViewModelBase, IRecipient<LibraryChangedMessage>
{
    private const int PageSize = 60;

    private readonly WallpaperActions _actions;
    private readonly PickerService _pickers;
    private readonly IThumbnailService _thumbnails;
    private readonly NotificationService _notifications;
    private readonly ILogger<LibraryViewModel> _logger;

    private WallpaperQuery _lastQuery = new();
    private int _totalCount;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _kindFilter = "All";

    [ObservableProperty]
    private bool _favoritesOnly;

    [ObservableProperty]
    private bool _pinnedOnly;

    [ObservableProperty]
    private bool _cachedOnly;

    [ObservableProperty]
    private string _sortLabel = "Newest first";

    [ObservableProperty]
    private string _summaryText = "Your library is empty. Import wallpapers to begin.";

    [ObservableProperty]
    private bool _canLoadMore;

    public LibraryViewModel(
        WallpaperActions actions,
        PickerService pickers,
        IThumbnailService thumbnails,
        NotificationService notifications,
        ILogger<LibraryViewModel> logger)
    {
        _actions = actions;
        _pickers = pickers;
        _thumbnails = thumbnails;
        _notifications = notifications;
        _logger = logger;
        WeakReferenceMessenger.Default.Register(this);
    }

    public ObservableCollection<WallpaperItemViewModel> Items { get; } = [];

    public IReadOnlyList<string> KindFilters { get; } = ["All", "Images", "Videos"];

    public IReadOnlyList<string> SortLabels { get; } =
        ["Newest first", "Recently used", "Most used", "Title A\u2013Z", "Largest first", "Random"];

    private static SortOrder TranslateSort(string label) => label switch
    {
        "Recently used" => SortOrder.RecentlyUsed,
        "Most used" => SortOrder.MostUsedFirst,
        "Title A\u2013Z" => SortOrder.TitleAZ,
        "Largest first" => SortOrder.LargestFirst,
        "Random" => SortOrder.Random,
        _ => SortOrder.NewestFirst,
    };

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
            var query = BuildQuery(skip: 0, take: PageSize);
            _lastQuery = query;
            var page = await _actions.Manager.QueryAsync(query).ConfigureAwait(true);
            _totalCount = page.TotalCount;
            var items = page.Items
                .Select(w => new WallpaperItemViewModel(w, _actions, Dispatcher))
                .ToArray();

            await RunOnUiAsync(() =>
            {
                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(item);
                }

                UpdateSummary(items.Length);
                return true;
            }).ConfigureAwait(true);

            await LoadThumbnailsAsync(items).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library load failed");
            _notifications.Error("The library could not be refreshed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsBusy || !CanLoadMore)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var query = _lastQuery.WithPaging(Items.Count, PageSize);
            var page = await _actions.Manager.QueryAsync(query).ConfigureAwait(true);
            var items = page.Items
                .Select(w => new WallpaperItemViewModel(w, _actions, Dispatcher))
                .ToArray();

            await RunOnUiAsync(() =>
            {
                foreach (var item in items)
                {
                    Items.Add(item);
                }

                UpdateSummary(Items.Count);
                return true;
            }).ConfigureAwait(true);

            await LoadThumbnailsAsync(items).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library load-more failed");
            _notifications.Error("More wallpapers could not be loaded.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var paths = await _pickers.PickWallpaperFilesAsync().ConfigureAwait(true);
        if (paths is { Count: > 0 })
        {
            await _actions.ImportAsync(paths).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync();

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        KindFilter = "All";
        FavoritesOnly = false;
        PinnedOnly = false;
        CachedOnly = false;
        SortLabel = "Newest first";
        _ = LoadAsync();
    }

    public WallpaperQuery BuildCurrentQuery() => BuildQuery(0, PageSize);

    private WallpaperQuery BuildQuery(int skip, int take)
    {
        var search = SearchText?.Trim();
        return new WallpaperQuery
        {
            SearchText = string.IsNullOrWhiteSpace(search) ? null : search,
            Kind = KindFilter switch
            {
                "Images" => WallpaperKind.Static,
                "Videos" => WallpaperKind.Video,
                _ => null,
            },
            FavoritesOnly = FavoritesOnly,
            PinnedOnly = PinnedOnly,
            CachedOnly = CachedOnly,
            SortBy = TranslateSort(SortLabel),
            Skip = skip,
            Take = take,
        };
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<WallpaperItemViewModel> items)
    {
        var gate = new SemaphoreSlim(4, 4);
        await Task.WhenAll(items.Select(async item =>
        {
            await gate.WaitAsync().ConfigureAwait(true);
            try
            {
                await item.LoadThumbnailAsync(_thumbnails).ConfigureAwait(true);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(true);
    }

    private void UpdateSummary(int shown)
    {
        SummaryText = _totalCount == 0
            ? "Your library is empty. Import wallpapers to begin."
            : $"Showing {shown} of {_totalCount}";
        CanLoadMore = shown < _totalCount;
        OnPropertyChanged(nameof(HasNoItems));
    }

    public bool HasNoItems => _totalCount == 0 && !IsBusy;

    partial void OnKindFilterChanged(string value) => _ = LoadAsync();

    partial void OnFavoritesOnlyChanged(bool value) => _ = LoadAsync();

    partial void OnPinnedOnlyChanged(bool value) => _ = LoadAsync();

    partial void OnCachedOnlyChanged(bool value) => _ = LoadAsync();

    partial void OnSortLabelChanged(string value) => _ = LoadAsync();

    public void Receive(LibraryChangedMessage message) => _ = LoadAsync();
}
