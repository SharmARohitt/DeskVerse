namespace Deskverse.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Deskverse.App.Messaging;
using Deskverse.App.Services;
using Deskverse.Application;
using Deskverse.Core;
using Microsoft.Extensions.Logging;

/// <summary>
/// Storage page: cache accounting, health, cleanup, reconciliation, and cache
/// directory relocation.
/// </summary>
public partial class StorageViewModel : ViewModelBase, IRecipient<LibraryChangedMessage>, IRecipient<EngineStateChangedMessage>
{
    private readonly StorageService _storage;
    private readonly NotificationService _notifications;
    private readonly PickerService _pickers;
    private readonly ILogger<StorageViewModel> _logger;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _cacheDirectory = string.Empty;

    [ObservableProperty]
    private string _limitText = "\u2014";

    [ObservableProperty]
    private string _usedText = "\u2014";

    [ObservableProperty]
    private double _usedPercent;

    [ObservableProperty]
    private string _cachedCountText = "\u2014";

    [ObservableProperty]
    private string _activeText = "\u2014";

    [ObservableProperty]
    private string _pinnedText = "\u2014";

    [ObservableProperty]
    private string _thumbnailText = "\u2014";

    [ObservableProperty]
    private string _diskText = "\u2014";

    [ObservableProperty]
    private string _healthText = "Unknown";

    [ObservableProperty]
    private string _healthMessage = "Waiting for the first storage scan\u2026";

    [ObservableProperty]
    private Microsoft.UI.Xaml.Controls.InfoBarSeverity _healthSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;

    public StorageViewModel(
        StorageService storage,
        NotificationService notifications,
        PickerService pickers,
        ILogger<StorageViewModel> logger)
    {
        _storage = storage;
        _notifications = notifications;
        _pickers = pickers;
        _logger = logger;
        WeakReferenceMessenger.Default.Register(this);
    }

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
            var status = await _storage.GetStatusAsync().ConfigureAwait(true);
            RunOnUi(() => ApplyStatus(status));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage status failed");
            _notifications.Error("Storage status could not be read.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyStatus(Core.Models.StorageStatus status)
    {
        CacheDirectory = status.CacheDirectory;
        LimitText = WallpaperItemViewModel.FormatBytes(status.LimitBytes);
        UsedText = WallpaperItemViewModel.FormatBytes(status.UsedBytes);
        UsedPercent = status.LimitBytes > 0
            ? Math.Clamp(status.UsedBytes * 100.0 / status.LimitBytes, 0, 100)
            : 0;
        CachedCountText = status.CachedEntryCount == 1
            ? "1 cached item"
            : $"{status.CachedEntryCount} cached items";
        ActiveText = WallpaperItemViewModel.FormatBytes(status.ActiveWallpaperBytes);
        PinnedText = WallpaperItemViewModel.FormatBytes(status.PinnedBytes);
        ThumbnailText = WallpaperItemViewModel.FormatBytes(status.ThumbnailBytes);
        DiskText = WallpaperItemViewModel.FormatBytes(status.AvailableDiskBytes);

        HealthText = status.Health switch
        {
            StorageHealth.Healthy => "Healthy",
            StorageHealth.Warning => "Warning",
            StorageHealth.Critical => "Critical",
            _ => "Unknown",
        };
        HealthMessage = string.IsNullOrWhiteSpace(status.HealthMessage)
            ? "The managed cache is within its limit."
            : status.HealthMessage;
        HealthSeverity = status.Health switch
        {
            StorageHealth.Healthy => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success,
            StorageHealth.Warning => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
            StorageHealth.Critical => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
            _ => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
        };
    }

    [RelayCommand]
    private async Task CleanupAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _storage.CleanupNowAsync().ConfigureAwait(true);
            _notifications.Success(result.Message ?? $"Cache cleanup freed {WallpaperItemViewModel.FormatBytes(result.FreedBytes)}.");
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual cleanup failed");
            _notifications.Error("Cleanup failed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReconcileAsync()
    {
        IsBusy = true;
        try
        {
            await _storage.ReconcileAsync().ConfigureAwait(true);
            _notifications.Success("Cache reconciled with the database.");
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconcile failed");
            _notifications.Error("Reconciliation failed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Called by the view after the user confirms a picked folder.</summary>
    public async Task ApplyCacheDirectoryAsync(string path)
    {
        IsBusy = true;
        try
        {
            var result = await _storage.ChangeCacheDirectoryAsync(path).ConfigureAwait(true);
            if (result.Success)
            {
                _notifications.Success($"Cache directory moved to {path}.");
                await LoadAsync().ConfigureAwait(true);
            }
            else
            {
                _notifications.Warning(result.Error ?? "The cache directory could not be changed.");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Runs the folder picker and returns the chosen path (or null).</summary>
    public async Task<string?> PickCacheDirectoryAsync()
    {
        return await _pickers.PickFolderAsync(CacheDirectory).ConfigureAwait(true);
    }

    public void Receive(LibraryChangedMessage message) => _ = LoadAsync();

    public void Receive(EngineStateChangedMessage message) => _ = LoadAsync();
}
