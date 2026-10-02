namespace Deskverse.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Deskverse.App.Messaging;
using Deskverse.App.Services;
using Deskverse.Application;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Home dashboard: current desktop state, engine controls, explainable
/// recommendations, and the Surprise Me shortcut.
/// </summary>
public partial class HomeViewModel : ViewModelBase, IRecipient<LibraryChangedMessage>, IRecipient<EngineStateChangedMessage>
{
    private readonly RecommendationService _recommendations;
    private readonly WallpaperActions _actions;
    private readonly PickerService _pickers;
    private readonly IThumbnailService _thumbnails;
    private readonly NotificationService _notifications;
    private readonly ILogger<HomeViewModel> _logger;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _engineStateText = "Idle";

    [ObservableProperty]
    private string _engineDetailText = "No wallpaper is currently active.";

    [ObservableProperty]
    private WallpaperItemViewModel? _activeWallpaper;

    [ObservableProperty]
    private string _greetingText = "Welcome to DeskVerse";

    [ObservableProperty]
    private string _librarySummaryText = string.Empty;

    public HomeViewModel(
        RecommendationService recommendations,
        WallpaperActions actions,
        PickerService pickers,
        IThumbnailService thumbnails,
        NotificationService notifications,
        ILogger<HomeViewModel> logger)
    {
        _recommendations = recommendations;
        _actions = actions;
        _pickers = pickers;
        _thumbnails = thumbnails;
        _notifications = notifications;
        _logger = logger;
        WeakReferenceMessenger.Default.Register(this);
        UpdateGreeting();
    }

    public ObservableCollection<RecommendationItemViewModel> Recommendations { get; } = [];

    public bool HasRecommendations => Recommendations.Count > 0;

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
            await RefreshEngineStatusAsync().ConfigureAwait(true);

            var items = await _actions.Manager.QueryAsync(new WallpaperQuery { Take = 200 }).ConfigureAwait(true);
            LibrarySummaryText = items.TotalCount == 1
                ? "1 wallpaper in your library"
                : $"{items.TotalCount} wallpapers in your library";

            var picks = await _recommendations.GetRecommendationsAsync(count: 8).ConfigureAwait(true);
            var recommendationItems = picks
                .Where(r => r.Wallpaper is not null)
                .Select(r => new RecommendationItemViewModel(
                    r,
                    new WallpaperItemViewModel(r.Wallpaper, _actions, Dispatcher)))
                .ToArray();

            await RunOnUiAsync(() =>
            {
                Recommendations.Clear();
                foreach (var item in recommendationItems)
                {
                    Recommendations.Add(item);
                }

                OnPropertyChanged(nameof(HasRecommendations));
            }).ConfigureAwait(true);

            foreach (var item in recommendationItems)
            {
                await item.Wallpaper.LoadThumbnailAsync(_thumbnails).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Home load failed");
            _notifications.Error("The home dashboard could not be refreshed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SurpriseMeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var (recommendation, outcome) = await _recommendations.SurpriseMeAsync().ConfigureAwait(true);
            if (outcome.Success)
            {
                _notifications.Success(
                    recommendation is null
                        ? "Surprise applied."
                        : $"Applied \u201C{recommendation.Wallpaper.Title}\u201D. {recommendation.Explanation}");
            }
            else
            {
                _notifications.Warning(outcome.Error ?? "Nothing suitable was found to apply.");
            }

            await RefreshEngineStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Surprise Me failed");
            _notifications.Error("Surprise Me failed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportWallpapersAsync()
    {
        var paths = await _pickers.PickWallpaperFilesAsync().ConfigureAwait(true);
        if (paths is { Count: > 0 })
        {
            await _actions.ImportAsync(paths).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task StopEngineAsync()
    {
        var result = await _actions.Manager.StopAsync().ConfigureAwait(true);
        if (result.Success)
        {
            _notifications.Info("Wallpaper playback stopped.");
        }
        else
        {
            _notifications.Warning(result.Error ?? "The engine could not be stopped.");
        }

        await RefreshEngineStatusAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PauseEngineAsync()
    {
        var result = await _actions.Manager.PauseAsync().ConfigureAwait(true);
        if (!result.Success)
        {
            _notifications.Warning(result.Error ?? "The engine could not be paused.");
        }

        await RefreshEngineStatusAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResumeEngineAsync()
    {
        var result = await _actions.Manager.ResumeAsync().ConfigureAwait(true);
        if (!result.Success)
        {
            _notifications.Warning(result.Error ?? "The engine could not be resumed.");
        }

        await RefreshEngineStatusAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestorePreviousAsync()
    {
        var result = await _actions.Manager.RestorePreviousAsync().ConfigureAwait(true);
        if (result.Success)
        {
            _notifications.Info("Restored the previously applied wallpaper.");
        }
        else
        {
            _notifications.Warning(result.Error ?? "There is no previous wallpaper to restore.");
        }

        await RefreshEngineStatusAsync().ConfigureAwait(true);
    }

    private async Task RefreshEngineStatusAsync()
    {
        var status = await _actions.GetEngineStatusAsync().ConfigureAwait(true);
        var active = status.ActiveWallpaperId is null
            ? null
            : await _actions.Manager.GetByIdAsync(status.ActiveWallpaperId.Value).ConfigureAwait(true);

        await RunOnUiAsync(() =>
        {
            EngineStateText = status.State switch
            {
                EngineState.Starting => "Starting",
                EngineState.Playing => "Playing",
                EngineState.Paused => "Paused",
                EngineState.Failed => "Failed",
                _ => "Idle",
            };

            EngineDetailText = status.ActiveWallpaperTitle is null
                ? "No wallpaper is currently active."
                : $"\u201C{status.ActiveWallpaperTitle}\u201D is on your desktop.";

            ActiveWallpaper = active is null
                ? ActiveWallpaper
                : new WallpaperItemViewModel(active, _actions, Dispatcher);
            return true;
        }).ConfigureAwait(true);

        if (ActiveWallpaper is not null)
        {
            await ActiveWallpaper.LoadThumbnailAsync(_thumbnails).ConfigureAwait(true);
        }
    }

    private void UpdateGreeting()
    {
        var hour = DateTimeOffset.Now.Hour;
        GreetingText = hour switch
        {
            < 5 => "A calm night deserves a calm desktop",
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening",
        };
    }

    public void Receive(LibraryChangedMessage message) => _ = LoadAsync();

    public void Receive(EngineStateChangedMessage message) => _ = RefreshEngineStatusAsync();
}
