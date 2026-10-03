namespace Deskverse.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deskverse.Api;
using Deskverse.App.Services;
using Deskverse.Application;
using Deskverse.Core;
using Deskverse.Core.Entities;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;

/// <summary>
/// Settings page: playback preferences, storage policy, startup behavior,
/// privacy, and local API credentials.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly PreferencesService _preferences;
    private readonly LocalApiState _apiState;
    private readonly ApiTokenStore _tokens;
    private readonly NotificationService _notifications;
    private readonly ILogger<SettingsViewModel> _logger;

    private UserPreferences _current = new();

    [ObservableProperty]

    private bool _isBusy;

    [ObservableProperty]

    private bool _allowVideoWallpapers= true;

    [ObservableProperty]

    private bool _pauseVideoOnBattery= true;

    [ObservableProperty]

    private bool _pauseVideoOnFullscreen= true;

    [ObservableProperty]

    private bool _animatedPreviewsEnabled= true;

    [ObservableProperty]

    private string _backgroundPolicyLabel= "Balanced";

    [ObservableProperty]

    private bool _autoCleanupEnabled= true;

    [ObservableProperty]

    private bool _runAtStartup;

    [ObservableProperty]

    private bool _minimizeToTray;

    [ObservableProperty]

    private bool _telemetryOptIn;

    [ObservableProperty]

    private string _storageLimitLabel= "2 GB";

    [ObservableProperty]

    private bool _hasBrightnessPreference;

    [ObservableProperty]

    private double _preferredBrightnessPercent= 50;

    [ObservableProperty]

    private string _preferredCategoriesText= string.Empty;

    [ObservableProperty]

    private string _preferredColorsText= string.Empty;

    [ObservableProperty]

    private string _preferredStylesText= string.Empty;

    [ObservableProperty]

    private string _apiBaseUrl= "http://127.0.0.1/\u2026";

    [ObservableProperty]

    private string _apiTokenDisplay= "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";

    [ObservableProperty]

    private bool _tokenRevealed;

    [ObservableProperty]

    private string _cacheDirectoryText= string.Empty;

    [ObservableProperty]

    private string _dataRootText= string.Empty;

    [ObservableProperty]

    private string _versionText= "DeskVerse 0.1.0";

    public SettingsViewModel(
        PreferencesService preferences,
        LocalApiState apiState,
        ApiTokenStore tokens,
        NotificationService notifications,
        ILogger<SettingsViewModel> logger)
    {
        _preferences = preferences;
        _apiState = apiState;
        _tokens = tokens;
        _notifications = notifications;
        _logger = logger;
    }

    public IReadOnlyList<string> BackgroundPolicyLabels { get; } =
        ["Conservative", "Balanced", "High quality"];

    public IReadOnlyList<string> StorageLimitLabels { get; } =
        ["1 GB", "2 GB", "4 GB", "8 GB", "16 GB", "32 GB", "64 GB"];

    private static long ParseStorageLimit(string label) => label switch
    {
        "1 GB" => 1L * 1024 * 1024 * 1024,
        "4 GB" => 4L * 1024 * 1024 * 1024,
        "8 GB" => 8L * 1024 * 1024 * 1024,
        "16 GB" => 16L * 1024 * 1024 * 1024,
        "32 GB" => 32L * 1024 * 1024 * 1024,
        "64 GB" => 64L * 1024 * 1024 * 1024,
        _ => 2L * 1024 * 1024 * 1024,
    };

    private static string FormatStorageLimit(long bytes) => bytes switch
    {
        >= 64L * 1024 * 1024 * 1024 => "64 GB",
        >= 32L * 1024 * 1024 * 1024 => "32 GB",
        >= 16L * 1024 * 1024 * 1024 => "16 GB",
        >= 8L * 1024 * 1024 * 1024 => "8 GB",
        >= 4L * 1024 * 1024 * 1024 => "4 GB",
        >= 1L * 1024 * 1024 * 1024 => "1 GB",
        _ => "2 GB",
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
            _current = await _preferences.LoadAsync().ConfigureAwait(true);
            var prefs = _current;
            var port = _apiState.Port;

            await RunOnUiAsync(() =>
            {
                AllowVideoWallpapers = prefs.AllowVideoWallpapers;
                PauseVideoOnBattery = prefs.PauseVideoOnBattery;
                PauseVideoOnFullscreen = prefs.PauseVideoOnFullscreen;
                AnimatedPreviewsEnabled = prefs.AnimatedPreviewsEnabled;
                BackgroundPolicyLabel = prefs.BackgroundPolicy switch
                {
                    BackgroundResourcePolicy.Conservative => "Conservative",
                    BackgroundResourcePolicy.HighQuality => "High quality",
                    _ => "Balanced",
                };
                AutoCleanupEnabled = prefs.AutoCleanupEnabled;
                RunAtStartup = ReadStartupRegistration();
                MinimizeToTray = prefs.MinimizeToTray;
                TelemetryOptIn = prefs.TelemetryOptIn;
                StorageLimitLabel = FormatStorageLimit(prefs.StorageLimitBytes);
                HasBrightnessPreference = prefs.PreferredBrightness is not null;
                PreferredBrightnessPercent = (prefs.PreferredBrightness ?? 0.5) * 100;
                PreferredCategoriesText = string.Join(", ", prefs.PreferredCategories);
                PreferredColorsText = string.Join(", ", prefs.PreferredColors);
                PreferredStylesText = string.Join(", ", prefs.PreferredStyles);
                CacheDirectoryText = prefs.CacheDirectory;
                DataRootText = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData) + "\\DeskVerse";
                ApiBaseUrl = port is null ? "Starting\u2026" : $"http://127.0.0.1:{port}/api/v1";
                return true;
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings load failed");
            _notifications.Error("Settings could not be loaded.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var prefs = _current;
            prefs.AllowVideoWallpapers = AllowVideoWallpapers;
            prefs.PauseVideoOnBattery = PauseVideoOnBattery;
            prefs.PauseVideoOnFullscreen = PauseVideoOnFullscreen;
            prefs.AnimatedPreviewsEnabled = AnimatedPreviewsEnabled;
            prefs.BackgroundPolicy = BackgroundPolicyLabel switch
            {
                "Conservative" => BackgroundResourcePolicy.Conservative,
                "High quality" => BackgroundResourcePolicy.HighQuality,
                _ => BackgroundResourcePolicy.Balanced,
            };
            prefs.AutoCleanupEnabled = AutoCleanupEnabled;
            prefs.MinimizeToTray = MinimizeToTray;
            prefs.TelemetryOptIn = TelemetryOptIn;
            prefs.StorageLimitBytes = ParseStorageLimit(StorageLimitLabel);
            prefs.PreferredBrightness = HasBrightnessPreference ? PreferredBrightnessPercent / 100 : null;
            prefs.PreferredCategories = SplitList(PreferredCategoriesText);
            prefs.PreferredColors = SplitList(PreferredColorsText);
            prefs.PreferredStyles = SplitList(PreferredStylesText);

            var result = await _preferences.SaveAsync(prefs).ConfigureAwait(true);
            if (result.Success)
            {
                _current = prefs;
                ApplyStartupRegistration(RunAtStartup);
                _notifications.Success("Settings saved.");
            }
            else
            {
                _notifications.Warning(result.Error ?? "The settings were not valid.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings save failed");
            _notifications.Error("Settings could not be saved.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleTokenReveal()
    {
        TokenRevealed = !TokenRevealed;
        ApiTokenDisplay = TokenRevealed ? _tokens.TokenHex : new string('\u2022', 16);
    }

    [RelayCommand]
    private void CopyToken()
    {
        RunOnUi(() =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(_tokens.TokenHex);
                Clipboard.SetContent(package);
                _notifications.Success("API token copied. Keep it private — it grants full local API access.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Clipboard copy failed");
                _notifications.Warning("The clipboard was unavailable.");
            }
        });
    }

    [RelayCommand]
    private void RotateToken()
    {
        _tokens.Rotate();
        TokenRevealed = false;
        ApiTokenDisplay = new string('\u2022', 16);
        _notifications.Success("A new API token was generated. Old tokens no longer work.");
    }

    [RelayCommand]
    private async Task ResetTasteProfileAsync()
    {
        try
        {
            var prefs = await _preferences.LoadAsync().ConfigureAwait(true);
            prefs.PreferredCategories = [];
            prefs.PreferredColors = [];
            prefs.PreferredStyles = [];
            prefs.PreferredBrightness = null;
            var result = await _preferences.SaveAsync(prefs).ConfigureAwait(true);
            if (result.Success)
            {
                PreferredCategoriesText = string.Empty;
                PreferredColorsText = string.Empty;
                PreferredStylesText = string.Empty;
                HasBrightnessPreference = false;
                _notifications.Success("Taste profile reset. Recommendations will rebuild from your usage history.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Taste profile reset failed");
            _notifications.Error("The taste profile could not be reset.");
        }
    }

    private static string[] SplitList(string text) => text
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(s => s.Length <= 40)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(20)
        .ToArray();

    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string RunValueName = "DeskVerse";

    private static bool ReadStartupRegistration()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(RunValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyStartupRegistration(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                var exePath = System.Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(RunValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup registration change failed");
            _notifications.Warning("Windows would not let DeskVerse change its startup registration.");
        }
    }
}
