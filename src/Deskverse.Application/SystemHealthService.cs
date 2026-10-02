namespace Deskverse.Application;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>Aggregated system health for the Storage/Settings pages and the API.</summary>
public sealed class SystemHealthService
{
    private readonly IWallpaperRepository _repository;
    private readonly IPreferencesStore _preferencesStore;
    private readonly IDisplayService _displayService;
    private readonly WallpaperManager _wallpaperManager;
    private readonly LocalApiState _apiState;
    private readonly IClock _clock;
    private readonly ILogger<SystemHealthService> _logger;

    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public SystemHealthService(
        IWallpaperRepository repository,
        IPreferencesStore preferencesStore,
        IDisplayService displayService,
        WallpaperManager wallpaperManager,
        LocalApiState apiState,
        IClock clock,
        ILogger<SystemHealthService> logger)
    {
        _repository = repository;
        _preferencesStore = preferencesStore;
        _displayService = displayService;
        _wallpaperManager = wallpaperManager;
        _apiState = apiState;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SystemHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        var databaseConnected = false;
        try
        {
            await _repository.CountAsync(cancellationToken).ConfigureAwait(false);
            databaseConnected = true;
        }
        catch (Exception ex)
        {
            warnings.Add($"Database is unreachable: {ex.Message}");
        }

        string cacheRoot;
        try
        {
            var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            cacheRoot = string.IsNullOrWhiteSpace(preferences.CacheDirectory)
                ? string.Empty
                : preferences.CacheDirectory;
        }
        catch (Exception ex)
        {
            cacheRoot = string.Empty;
            warnings.Add($"Preferences could not be read: {ex.Message}");
        }

        var cacheAvailable = !string.IsNullOrEmpty(cacheRoot) && Directory.Exists(cacheRoot);
        if (!cacheAvailable)
        {
            warnings.Add("The managed cache folder is not reachable.");
        }

        EngineStatus engineStatus;
        try
        {
            engineStatus = await _wallpaperManager.GetEngineStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            engineStatus = new EngineStatus(EngineState.Failed, null, null, null, null, ex.Message);
            warnings.Add($"The wallpaper engine reported an error: {ex.Message}");
        }

        if (engineStatus.State == EngineState.Failed && engineStatus.LastError is not null)
        {
            warnings.Add($"Wallpaper engine: {engineStatus.LastError}");
        }

        var displayCount = 0;
        try
        {
            displayCount = _displayService.GetDisplays().Count;
        }
        catch (Exception ex)
        {
            warnings.Add($"Displays could not be enumerated: {ex.Message}");
        }

        return new SystemHealth(
            databaseConnected,
            cacheAvailable,
            _apiState.IsListening,
            _apiState.Port,
            _wallpaperManager.ActiveWallpaperId,
            engineStatus.State,
            _clock.UtcNow - _startedAt,
            displayCount,
            warnings);
    }
}
