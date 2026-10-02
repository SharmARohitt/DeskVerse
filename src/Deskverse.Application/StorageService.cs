namespace Deskverse.Application;

using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Deskverse.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Storage management surface for the UI and API: status, on-demand cleanup,
/// crash reconciliation, and relocating the managed cache directory safely.
/// </summary>
public sealed class StorageService
{
    private readonly CacheManager _cacheManager;
    private readonly PreferencesCachePolicy _policy;
    private readonly IPreferencesStore _preferencesStore;
    private readonly IWallpaperRepository _repository;
    private readonly WallpaperManager _wallpaperManager;
    private readonly ILogger<StorageService> _logger;

    public StorageService(
        CacheManager cacheManager,
        PreferencesCachePolicy policy,
        IPreferencesStore preferencesStore,
        IWallpaperRepository repository,
        WallpaperManager wallpaperManager,
        ILogger<StorageService> logger)
    {
        _cacheManager = cacheManager;
        _policy = policy;
        _preferencesStore = preferencesStore;
        _repository = repository;
        _wallpaperManager = wallpaperManager;
        _logger = logger;
    }

    public Task<StorageStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        _cacheManager.GetStatusAsync(_wallpaperManager.ActiveWallpaperId, cancellationToken);

    /// <summary>Runs an LRU cleanup pass now, protecting active and pinned content.</summary>
    public async Task<CacheCleanupResult> CleanupNowAsync(CancellationToken cancellationToken = default)
    {
        var result = await _cacheManager
            .EnsureSpaceAsync(0, _wallpaperManager.ActiveWallpaperId, cancellationToken)
            .ConfigureAwait(false);
        if (result.FreedBytes > 0)
        {
            _logger.LogInformation("Manual cleanup freed {Bytes:N0} bytes.", result.FreedBytes);
        }

        return result;
    }

    /// <summary>Repairs cache/database divergence after crashes or external tampering.</summary>
    public Task ReconcileAsync(CancellationToken cancellationToken = default) =>
        _cacheManager.ReconcileAsync(cancellationToken);

    /// <summary>
    /// Moves the managed cache to a new directory. Files are moved one by one with
    /// copy-fallback so an interruption never loses content; the database keeps
    /// relative paths, which are unchanged by the move.
    /// </summary>
    public async Task<OperationResult> ChangeCacheDirectoryAsync(
        string newRoot,
        CancellationToken cancellationToken = default)
    {
        var validationError = CacheDirectoryRules.Validate(newRoot);
        if (validationError is not null)
        {
            return OperationResult.Fail(validationError);
        }

        var fullNewRoot = Path.GetFullPath(newRoot.Trim());
        if (string.Equals(fullNewRoot, _policy.CacheRoot, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail("The cache already lives in that folder.");
        }

        var prepareError = CacheDirectoryRules.TryPrepare(fullNewRoot);
        if (prepareError is not null)
        {
            return OperationResult.Fail(prepareError);
        }

        var oldRoot = _policy.CacheRoot;
        var cached = await _repository
            .GetCachedOrderedByLeastRecentUseAsync(cancellationToken)
            .ConfigureAwait(false);

        var moved = 0;
        var failed = new List<string>();
        foreach (var wallpaper in cached)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var oldAbsolute = _cacheManager.ResolveCachePath(wallpaper.CacheRelativePath);
            if (oldAbsolute is null)
            {
                continue;
            }

            var newAbsolute = Path.Combine(fullNewRoot,
                wallpaper.CacheRelativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (File.Exists(oldAbsolute))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newAbsolute)!);
                    File.Move(oldAbsolute, newAbsolute, overwrite: true);
                }
                else if (!File.Exists(newAbsolute))
                {
                    // Row claims cached but the payload is gone; reconcile will fix it.
                    continue;
                }

                moved++;
            }
            catch (IOException ex)
            {
                // Fall back to copy+delete for locked or cross-volume files.
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newAbsolute)!);
                    File.Copy(oldAbsolute, newAbsolute, overwrite: true);
                    File.Delete(oldAbsolute);
                    moved++;
                }
                catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
                {
                    failed.Add($"'{wallpaper.Title}': {ex.Message}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                failed.Add($"'{wallpaper.Title}': {ex.Message}");
            }
        }

        var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        preferences.CacheDirectory = fullNewRoot;
        preferences.UpdatedAt = DateTimeOffset.UtcNow;
        await _preferencesStore.SaveAsync(preferences, cancellationToken).ConfigureAwait(false);
        _policy.Refresh(preferences);

        await _cacheManager.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Cache directory moved from {Old} to {New}: {Moved} files moved, {Failed} failures.",
            oldRoot, fullNewRoot, moved, failed.Count);

        if (failed.Count > 0)
        {
            return OperationResult.Fail(
                $"Moved {moved} files, but {failed.Count} could not be moved: {string.Join("; ", failed.Take(5))}");
        }

        return OperationResult.Ok();
    }
}
