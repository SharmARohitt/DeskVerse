namespace Deskverse.Application;

using System.Diagnostics;
using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background wallpaper rotation engine. Advances the wallpaper on a configurable
/// interval according to the user's chosen mode. Survives sleep/resume by checking
/// elapsed time rather than relying on timer precision. Prevents duplicate
/// scheduler instances by refusing to start when already running.
/// </summary>
public sealed class RotationScheduler : IAsyncDisposable
{
    private readonly IPreferencesStore _preferencesStore;
    private readonly IWallpaperRepository _repository;
    private readonly ICollectionRepository _collectionRepository;
    private readonly RecommendationService _recommendationService;
    private readonly WallpaperManager _wallpaperManager;
    private readonly ILogger<RotationScheduler> _logger;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextAt = DateTimeOffset.MaxValue;

    // Sequential rotation: index into the ordered candidate list.
    private int _sequentialIndex;

    public RotationScheduler(
        IPreferencesStore preferencesStore,
        IWallpaperRepository repository,
        ICollectionRepository collectionRepository,
        RecommendationService recommendationService,
        WallpaperManager wallpaperManager,
        ILogger<RotationScheduler> logger)
    {
        _preferencesStore = preferencesStore;
        _repository = repository;
        _collectionRepository = collectionRepository;
        _recommendationService = recommendationService;
        _wallpaperManager = wallpaperManager;
        _logger = logger;
    }

    public event EventHandler? Ticked;

    public bool IsRunning { get; private set; }

    /// <summary>The UTC time the next automatic rotation will fire.</summary>
    public DateTimeOffset NextAt => _nextAt;

    /// <summary>Starts the scheduler. No-op if already running.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                return;
            }

            var prefs = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!prefs.RotationEnabled)
            {
                _logger.LogInformation("Rotation scheduler start skipped (disabled in preferences).");
                return;
            }

            _cts = new CancellationTokenSource();
            _nextAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(15, prefs.RotationIntervalSeconds));
            _loop = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
            IsRunning = true;
            _logger.LogInformation(
                "Rotation scheduler started (interval {Seconds}s, mode {Mode}).",
                prefs.RotationIntervalSeconds, prefs.RotationMode);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops the scheduler gracefully.</summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRunning || _cts is null)
            {
                return;
            }

            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await (_loop ?? Task.CompletedTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }

            _cts.Dispose();
            _cts = null;
            _loop = null;
            _nextAt = DateTimeOffset.MaxValue;
            IsRunning = false;
            _logger.LogInformation("Rotation scheduler stopped.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Restarts with the latest preferences — call after settings are saved.</summary>
    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Forces an immediate advance without waiting for the interval.</summary>
    public async Task AdvanceNowAsync(CancellationToken cancellationToken = default)
    {
        await ApplyNextAsync(cancellationToken).ConfigureAwait(false);
        var prefs = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        _nextAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(15, prefs.RotationIntervalSeconds));
        Ticked?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var prefs = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                var delay = _nextAt - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    // Sleep in 5-second chunks to remain responsive to sleep/resume.
                    var chunk = TimeSpan.FromSeconds(5);
                    while (delay > TimeSpan.Zero && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(delay < chunk ? delay : chunk, cancellationToken).ConfigureAwait(false);
                        delay = _nextAt - DateTimeOffset.UtcNow;

                        // After wake-from-sleep the wall clock jumps; recalculate.
                        var elapsed = DateTimeOffset.UtcNow - (_nextAt - TimeSpan.FromSeconds(prefs.RotationIntervalSeconds));
                        if (elapsed > TimeSpan.FromSeconds(prefs.RotationIntervalSeconds * 2))
                        {
                            _logger.LogInformation("System resume detected; advancing rotation immediately.");
                            break;
                        }
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Re-read preferences at each tick so interval/mode changes take effect.
                prefs = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (!prefs.RotationEnabled)
                {
                    _logger.LogInformation("Rotation disabled; stopping loop.");
                    break;
                }

                await ApplyNextAsync(cancellationToken).ConfigureAwait(false);
                _nextAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(15, prefs.RotationIntervalSeconds));
                Ticked?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rotation scheduler loop error; retrying in 60 s.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        IsRunning = false;
    }

    private async Task ApplyNextAsync(CancellationToken cancellationToken)
    {
        var prefs = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        Wallpaper? next = prefs.RotationMode switch
        {
            RotationMode.FavoritesOnly => await PickRandomAsync(
                new WallpaperQuery { FavoritesOnly = true, CachedOnly = true, ExcludeDisliked = true, Take = 200 },
                cancellationToken).ConfigureAwait(false),
            RotationMode.Recommended => await PickRecommendedAsync(cancellationToken).ConfigureAwait(false),
            RotationMode.Collection => await PickFromCollectionAsync(prefs.RotationCollectionId, cancellationToken).ConfigureAwait(false),
            RotationMode.Sequential => await PickSequentialAsync(cancellationToken).ConfigureAwait(false),
            _ => await PickRandomAsync(
                new WallpaperQuery { CachedOnly = true, ExcludeDisliked = true, Take = 200 },
                cancellationToken).ConfigureAwait(false),
        };

        if (next is null)
        {
            _logger.LogWarning("Rotation found no eligible wallpaper for mode {Mode}.", prefs.RotationMode);
            return;
        }

        if (next.Id == _wallpaperManager.ActiveWallpaperId)
        {
            _logger.LogDebug("Rotation skipped — candidate is already active.");
            return;
        }

        var outcome = await _wallpaperManager.ApplyAsync(next.Id, null, cancellationToken).ConfigureAwait(false);
        if (!outcome.Success)
        {
            _logger.LogWarning("Rotation apply failed for '{Title}': {Error}", next.Title, outcome.Error);
        }
        else
        {
            _logger.LogInformation("Rotation applied '{Title}' (mode {Mode}).", next.Title, prefs.RotationMode);
        }
    }

    private async Task<Wallpaper?> PickRandomAsync(WallpaperQuery baseQuery, CancellationToken cancellationToken)
    {
        var pool = await _repository.QueryAsync(baseQuery with { SortBy = SortOrder.Random }, cancellationToken).ConfigureAwait(false);
        return pool.Items.FirstOrDefault();
    }

    private async Task<Wallpaper?> PickRecommendedAsync(CancellationToken cancellationToken)
    {
        var recommendations = await _recommendationService.GetRecommendationsAsync(5, cancellationToken).ConfigureAwait(false);
        foreach (var r in recommendations)
        {
            if (r.Wallpaper.IsCached && r.Wallpaper.Id != _wallpaperManager.ActiveWallpaperId)
            {
                return r.Wallpaper;
            }
        }

        // Fall back to random if no recommendation is available.
        return await PickRandomAsync(
            new WallpaperQuery { CachedOnly = true, ExcludeDisliked = true, Take = 200 },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Wallpaper?> PickFromCollectionAsync(Guid? collectionId, CancellationToken cancellationToken)
    {
        if (collectionId is null)
        {
            return await PickRandomAsync(
                new WallpaperQuery { CachedOnly = true, ExcludeDisliked = true, Take = 200 },
                cancellationToken).ConfigureAwait(false);
        }

        var collection = await _collectionRepository.GetAsync(collectionId.Value, cancellationToken).ConfigureAwait(false);
        if (collection is null || collection.Items.Count == 0)
        {
            return null;
        }

        var candidates = collection.Items
            .Where(i => i.Wallpaper?.IsCached == true && i.Wallpaper.Id != _wallpaperManager.ActiveWallpaperId)
            .Select(i => i.Wallpaper!)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[Random.Shared.Next(candidates.Count)];
    }

    private async Task<Wallpaper?> PickSequentialAsync(CancellationToken cancellationToken)
    {
        var page = await _repository.QueryAsync(
            new WallpaperQuery { CachedOnly = true, ExcludeDisliked = true, SortBy = SortOrder.TitleAZ, Take = 500 },
            cancellationToken).ConfigureAwait(false);

        if (page.Items.Count == 0)
        {
            return null;
        }

        // Wrap-around and skip the currently-active one.
        var count = page.Items.Count;
        for (var attempt = 0; attempt < count; attempt++)
        {
            _sequentialIndex = (_sequentialIndex + 1) % count;
            var candidate = page.Items[_sequentialIndex];
            if (candidate.Id != _wallpaperManager.ActiveWallpaperId)
            {
                return candidate;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
