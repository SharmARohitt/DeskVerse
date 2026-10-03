namespace Deskverse.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deskverse.App.Services;
using Deskverse.Application;
using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Deskverse.Providers;
using Microsoft.Extensions.Logging;

/// <summary>
/// Discover page: provider catalog browsing with search, trending, category
/// chips, and secure downloads into the managed cache.
/// </summary>
public partial class DiscoverViewModel : ViewModelBase
{
    private readonly DiscoveryService _discovery;
    private readonly WallpaperActions _actions;
    private readonly NotificationService _notifications;
    private readonly ILogger<DiscoverViewModel> _logger;

    [ObservableProperty]

    private bool _isBusy;

    [ObservableProperty]

    private string _searchText= string.Empty;

    [ObservableProperty]

    private string _statusText= "Browsing trending wallpapers\u2026";

    [ObservableProperty]

    private bool _hasFailures;

    [ObservableProperty]

    private string _failureSummary= string.Empty;

    [ObservableProperty]

    private bool _hasResults;

    public DiscoverViewModel(
        DiscoveryService discovery,
        WallpaperActions actions,
        NotificationService notifications,
        ILogger<DiscoverViewModel> logger)
    {
        _discovery = discovery;
        _actions = actions;
        _notifications = notifications;
        _logger = logger;
    }

    public ObservableCollection<ProviderWallpaperItemViewModel> Results { get; } = [];

    public ObservableCollection<ProviderInfo> Providers { get; } = [];

    public ObservableCollection<string> Categories { get; } = [];

    public IReadOnlyList<string> KindFilters { get; } = ["All", "Images", "Videos"];

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
            var providers = _discovery.GetProviders();
            await RunOnUiAsync(() =>
            {
                Providers.Clear();
                foreach (var provider in providers)
                {
                    Providers.Add(provider);
                }

                return true;
            }).ConfigureAwait(true);

            var categories = await _discovery.GetCategoriesAsync().ConfigureAwait(true);
            if (categories.Items is { Count: > 0 })
            {
                await RunOnUiAsync(() =>
                {
                    Categories.Clear();
                    foreach (var category in categories.Items.Take(12))
                    {
                        Categories.Add(category);
                    }

                    return true;
                }).ConfigureAwait(true);
            }

            await LoadTrendingAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discover load failed");
            StatusText = "Discovery is unavailable right now.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        var query = SearchText?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            await LoadTrendingAsync().ConfigureAwait(true);
            return;
        }

        await RunQueryAsync(
            () => _discovery.SearchAsync(new WallpaperQuery
            {
                SearchText = query,
                Take = 24,
                ExcludeDisliked = false,
            }),
            $"Results for \u201C{query}\u201D").ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task LoadTrendingAsync()
    {
        await RunQueryAsync(
            () => _discovery.GetTrendingAsync(24),
            "Trending right now").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SearchCategoryAsync(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        SearchText = category;
        await SearchAsync().ConfigureAwait(true);
    }

    public async Task DownloadItemAsync(ProviderWallpaperItemViewModel item)
    {
        if (item.IsDownloading)
        {
            return;
        }

        item.IsDownloading = true;
        try
        {
            var outcome = await _discovery
                .DownloadAsync(item.Model.ProviderId, item.Model.SourceId)
                .ConfigureAwait(true);
            if (outcome.Success)
            {
                _notifications.Success(
                    outcome.WasAlreadyCached
                        ? $"\u201C{item.Title}\u201D was already in your library."
                        : $"Downloaded \u201C{item.Title}\u201D into your library.");
            }
            else
            {
                _notifications.Warning(outcome.Error ?? "The download failed.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discover download failed for {Provider}/{SourceId}", item.Model.ProviderId, item.Model.SourceId);
            _notifications.Error("The download failed unexpectedly.");
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    private async Task RunQueryAsync(
        Func<Task<AggregatedResult<ProviderWallpaper>>> query,
        string successLabel)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await query().ConfigureAwait(true);
            var items = result.Items
                .Select(p => new ProviderWallpaperItemViewModel(
                    p,
                    ProviderDisplayName(p.ProviderId),
                    this))
                .ToArray();

            var failureSummary = result.Failures is { Count: > 0 }
                ? string.Join("\n", result.Failures.Select(f => $"{f.DisplayName}: {f.Error}"))
                : string.Empty;

            await RunOnUiAsync(() =>
            {
                Results.Clear();
                foreach (var item in items)
                {
                    Results.Add(item);
                }

                HasResults = items.Length > 0;
                StatusText = items.Length == 0
                    ? "Nothing matched. Try another search."
                    : $"{successLabel} \u2014 {items.Length} result{(items.Length == 1 ? string.Empty : "s")}";
                HasFailures = result.Failures is { Count: > 0 };
                FailureSummary = failureSummary;
                return true;
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discover query failed");
            StatusText = "The search could not be completed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string ProviderDisplayName(string providerId) =>
        Providers.FirstOrDefault(p => p.ProviderId == providerId)?.DisplayName ?? providerId;
}
