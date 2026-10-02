namespace Deskverse.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deskverse.App.Services;
using Deskverse.Application;
using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Microsoft.Extensions.Logging;

/// <summary>
/// Rotation scheduler surface: start, stop, advance, mode, interval, and
/// collection selection. Shown as a panel inside SettingsView.
/// </summary>
public partial class RotationViewModel : ViewModelBase
{
    private readonly RotationScheduler _scheduler;
    private readonly CollectionsService _collections;
    private readonly IPreferencesStore _preferencesStore;
    private readonly NotificationService _notifications;
    private readonly ILogger<RotationViewModel> _logger;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _rotationModeLabel = "Random";

    [ObservableProperty]
    private string _intervalLabel = "1 hour";

    [ObservableProperty]
    private string _nextAtText = "—";

    [ObservableProperty]
    private CollectionTileViewModel? _selectedCollection;

    [ObservableProperty]
    private bool _isBusy;

    public RotationViewModel(
        RotationScheduler scheduler,
        CollectionsService collections,
        IPreferencesStore preferencesStore,
        NotificationService notifications,
        ILogger<RotationViewModel> logger)
    {
        _scheduler = scheduler;
        _collections = collections;
        _preferencesStore = preferencesStore;
        _notifications = notifications;
        _logger = logger;
        _scheduler.Ticked += OnTicked;
    }

    public IReadOnlyList<string> ModesLabels { get; } =
        ["Random", "Sequential", "Favorites only", "Recommended", "Collection"];

    public IReadOnlyList<string> IntervalLabels { get; } =
        ["15 minutes", "30 minutes", "1 hour", "2 hours", "4 hours", "Daily", "Custom"];

    public ObservableCollection<CollectionTileViewModel> Collections { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var prefs = await _preferencesStore.LoadAsync().ConfigureAwait(true);
            var cols = await _collections.GetAllAsync().ConfigureAwait(true);

            await RunOnUiAsync(() =>
            {
                IsEnabled = prefs.RotationEnabled;
                IsRunning = _scheduler.IsRunning;
                RotationModeLabel = ModeToLabel(prefs.RotationMode);
                IntervalLabel = SecondsToLabel(prefs.RotationIntervalSeconds);
                RefreshNextAt();

                Collections.Clear();
                foreach (var c in cols)
                {
                    Collections.Add(new CollectionTileViewModel(c, null!));
                }

                SelectedCollection = prefs.RotationCollectionId is { } cid
                    ? Collections.FirstOrDefault(c => c.Id == cid)
                    : null;

                return true;
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RotationViewModel load failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAndApplyAsync()
    {
        IsBusy = true;
        try
        {
            var prefs = await _preferencesStore.LoadAsync().ConfigureAwait(true);
            prefs.RotationEnabled = IsEnabled;
            prefs.RotationMode = LabelToMode(RotationModeLabel);
            prefs.RotationIntervalSeconds = LabelToSeconds(IntervalLabel);
            prefs.RotationCollectionId = SelectedCollection?.Id;
            await _preferencesStore.SaveAsync(prefs).ConfigureAwait(true);

            await _scheduler.RestartAsync().ConfigureAwait(true);
            IsRunning = _scheduler.IsRunning;
            RefreshNextAt();
            _notifications.Success(IsEnabled ? "Rotation scheduler started." : "Rotation scheduler stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rotation save failed");
            _notifications.Error("Could not save rotation settings.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AdvanceNowAsync()
    {
        try
        {
            await _scheduler.AdvanceNowAsync().ConfigureAwait(true);
            RefreshNextAt();
            _notifications.Info("Rotated to the next wallpaper.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Advance now failed");
            _notifications.Warning("Could not advance to the next wallpaper.");
        }
    }

    private void OnTicked(object? sender, EventArgs e) => RunOnUi(RefreshNextAt);

    private void RefreshNextAt()
    {
        IsRunning = _scheduler.IsRunning;
        if (!_scheduler.IsRunning || _scheduler.NextAt == DateTimeOffset.MaxValue)
        {
            NextAtText = "—";
            return;
        }

        var remaining = _scheduler.NextAt - DateTimeOffset.UtcNow;
        NextAtText = remaining <= TimeSpan.Zero
            ? "Now"
            : remaining.TotalHours >= 1
                ? $"{remaining:h\\:mm\\:ss}"
                : $"{remaining:mm\\:ss}";
    }

    private static string ModeToLabel(RotationMode mode) => mode switch
    {
        RotationMode.Sequential => "Sequential",
        RotationMode.FavoritesOnly => "Favorites only",
        RotationMode.Recommended => "Recommended",
        RotationMode.Collection => "Collection",
        _ => "Random",
    };

    private static RotationMode LabelToMode(string label) => label switch
    {
        "Sequential" => RotationMode.Sequential,
        "Favorites only" => RotationMode.FavoritesOnly,
        "Recommended" => RotationMode.Recommended,
        "Collection" => RotationMode.Collection,
        _ => RotationMode.Random,
    };

    private static string SecondsToLabel(int seconds) => seconds switch
    {
        <= 15 * 60 => "15 minutes",
        <= 30 * 60 => "30 minutes",
        <= 2 * 3600 => seconds <= 3600 ? "1 hour" : "2 hours",
        <= 4 * 3600 => "4 hours",
        >= 86400 => "Daily",
        _ => "1 hour",
    };

    private static int LabelToSeconds(string label) => label switch
    {
        "15 minutes" => 15 * 60,
        "30 minutes" => 30 * 60,
        "2 hours" => 2 * 3600,
        "4 hours" => 4 * 3600,
        "Daily" => 86400,
        _ => 3600,
    };
}
