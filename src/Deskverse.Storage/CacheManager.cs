namespace Deskverse.Storage;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Deskverse.Security.Paths;
using Microsoft.Extensions.Logging;

/// <summary>
/// Managed wallpaper cache: accounting, LRU eviction that protects the active
/// and pinned wallpapers, atomic promotion of staged files, and reconciliation
/// after crashes or interrupted transfers.
/// </summary>
public sealed class CacheManager
{
    private readonly IWallpaperRepository _repository;
    private readonly ICachePolicy _policy;
    private readonly ILogger<CacheManager> _logger;

    public CacheManager(
        IWallpaperRepository repository,
        ICachePolicy policy,
        ILogger<CacheManager> logger)
    {
        _repository = repository;
        _policy = policy;
        _logger = logger;
    }

    public string CacheRoot => _policy.CacheRoot;

    public long LimitBytes => _policy.LimitBytes;

    /// <summary>Resolves a wallpaper's cache-relative path to an absolute path, verifying containment.</summary>
    public string? ResolveCachePath(string cacheRelativePath)
    {
        if (string.IsNullOrWhiteSpace(cacheRelativePath))
        {
            return null;
        }

        return SafePath.SafeCombine(_policy.CacheRoot, cacheRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public Task<StorageStatus> GetStatusAsync(Guid? activeWallpaperId, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var used = DiskAccounting.MeasureDirectory(_policy.CacheRoot);
            var cached = _repository.GetCachedOrderedByLeastRecentUseAsync(cancellationToken).GetAwaiter().GetResult();

            long pinnedBytes = 0;
            long activeBytes = 0;
            var entryCount = 0;
            foreach (var wallpaper in cached)
            {
                entryCount++;
                if (wallpaper.IsPinned)
                {
                    pinnedBytes += wallpaper.FileSizeBytes;
                }

                if (activeWallpaperId.HasValue && wallpaper.Id == activeWallpaperId.Value)
                {
                    activeBytes += wallpaper.FileSizeBytes;
                }
            }

            var available = DiskAccounting.GetAvailableDiskBytes(_policy.CacheRoot);
            var (health, message) = EvaluateHealth(used, _policy.LimitBytes, available);

            return new StorageStatus(
                _policy.CacheRoot,
                _policy.LimitBytes,
                used,
                ThumbnailBytes: 0,
                AvailableDiskBytes: available ?? -1,
                ActiveWallpaperBytes: activeBytes,
                PinnedBytes: pinnedBytes,
                entryCount,
                health,
                message);
        }, cancellationToken);
    }

    private static (StorageHealth, string) EvaluateHealth(long used, long limit, long? available)
    {
        if (limit <= 0)
        {
            return (StorageHealth.Warning, "Cache limit is not configured.");
        }

        var ratio = (double)used / limit;
        if (ratio >= 0.95)
        {
            return (StorageHealth.Critical, $"Cache is {ratio:P0} full. Cleanup or a higher limit is required.");
        }

        if (ratio >= 0.85)
        {
            return (StorageHealth.Warning, $"Cache is {ratio:P0} full. Automatic cleanup will start soon.");
        }

        if (available is { } free && free < 2L * 1024 * 1024 * 1024)
        {
            return (StorageHealth.Warning, $"Only {free / (1024.0 * 1024 * 1024):N1} GB free on the cache drive.");
        }

        return (StorageHealth.Healthy, "Cache usage is within limits.");
    }

    /// <summary>
    /// Evicts least-recently-used wallpapers until <paramref name="incomingBytes"/>
    /// fit under the limit. The active wallpaper and pinned content are never evicted.
    /// </summary>
    public Task<CacheCleanupResult> EnsureSpaceAsync(long incomingBytes, Guid? activeWallpaperId, CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var used = DiskAccounting.MeasureDirectory(_policy.CacheRoot);
            var projected = used + Math.Max(0, incomingBytes);
            if (projected <= _policy.LimitBytes)
            {
                return new CacheCleanupResult
                {
                    EnoughSpace = true,
                    FreedBytes = 0,
                    ProjectedUsedBytes = projected,
                };
            }

            var candidates = await _repository.GetCachedOrderedByLeastRecentUseAsync(cancellationToken).ConfigureAwait(false);
            // Two tiers: non-favorites are evicted first, favorites last, each in LRU order.
            var ordered = candidates
                .Where(w => !w.IsPinned && (activeWallpaperId is null || w.Id != activeWallpaperId))
                .OrderBy(w => w.IsFavorite)
                .ThenBy(w => w.CacheLastAccessTicks)
                .ToList();

            var removed = new List<string>();
            var evictIds = new List<Guid>();
            long freed = 0;
            foreach (var wallpaper in ordered)
            {
                if (projected - freed <= _policy.LimitBytes)
                {
                    break;
                }

                var absolute = ResolveCachePath(wallpaper.CacheRelativePath);
                if (absolute is null)
                {
                    continue;
                }

                if (!SafePath.TrySafeDeleteFile(_policy.CacheRoot, absolute))
                {
                    _logger.LogWarning("Cache eviction failed for {Wallpaper}: file could not be deleted safely", wallpaper.Id);
                    continue;
                }

                freed += wallpaper.FileSizeBytes;
                evictIds.Add(wallpaper.Id);
                removed.Add(wallpaper.Title);
                _logger.LogInformation("Evicted cached wallpaper {Title} ({Bytes:N0} bytes) to make room", wallpaper.Title, wallpaper.FileSizeBytes);
            }

            if (evictIds.Count > 0)
            {
                // Files are already gone; the database must agree. Rows that keep
                // claiming to be cached are repaired by ReconcileAsync on next start.
                await _repository.MarkUncachedAsync(evictIds, cancellationToken).ConfigureAwait(false);
            }

            var finalProjected = projected - freed;
            if (finalProjected > _policy.LimitBytes)
            {
                return CacheCleanupResult.Insufficient(
                    finalProjected,
                    _policy.LimitBytes,
                    $"Cannot free enough space: {finalProjected / (1024.0 * 1024 * 1024):N2} GB needed against a {_policy.LimitBytes / (1020.0 * 1024 * 1024):N1} GB limit. Increase the limit, choose another folder, or cancel.");
            }

            return new CacheCleanupResult
            {
                EnoughSpace = true,
                FreedBytes = freed,
                ProjectedUsedBytes = finalProjected,
                RemovedTitles = removed,
                Message = removed.Count > 0
                    ? $"Freed {freed / (1024.0 * 1024):N1} MB by removing {removed.Count} least-recently-used wallpapers."
                    : null,
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Moves a validated staged file into the cache atomically. Cross-volume
    /// moves are staged inside the destination first so the final rename is atomic.
    /// </summary>
    public async Task<OperationResult> PromoteToCacheAsync(string stagedPath, string cacheRelativePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(stagedPath))
        {
            return OperationResult.Fail("Staged file is missing.");
        }

        var destination = ResolveCachePath(cacheRelativePath);
        if (destination is null)
        {
            return OperationResult.Fail("Cache path escapes the cache root.");
        }

        try
        {
            var destinationDirectory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(destinationDirectory);

            var sameVolume = string.Equals(
                Path.GetPathRoot(Path.GetFullPath(stagedPath)),
                Path.GetPathRoot(destination),
                StringComparison.OrdinalIgnoreCase);

            if (sameVolume)
            {
                File.Move(stagedPath, destination, overwrite: true);
            }
            else
            {
                var staging = destination + ".promoting";
                await CopyFileAsync(stagedPath, staging, cancellationToken).ConfigureAwait(false);
                File.Move(staging, destination, overwrite: true);
                File.Delete(stagedPath);
            }

            return OperationResult.Ok();
        }
        catch (IOException ex)
        {
            return OperationResult.Fail($"Could not store file in the cache: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail($"Access denied writing to the cache: {ex.Message}");
        }
    }

    /// <summary>Removes one wallpaper's payload from the cache. Refuses to touch the active or pinned ones.</summary>
    public Task<CacheRemovalResult> RemoveFromCacheAsync(Wallpaper wallpaper, Guid? activeWallpaperId, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (wallpaper.IsPinned)
            {
                return new CacheRemovalResult { Success = false, Error = "Pinned wallpapers cannot be removed from the cache." };
            }

            if (activeWallpaperId.HasValue && wallpaper.Id == activeWallpaperId.Value)
            {
                return new CacheRemovalResult { Success = false, Error = "The active wallpaper cannot be removed from the cache." };
            }

            var absolute = ResolveCachePath(wallpaper.CacheRelativePath);
            if (absolute is null || !File.Exists(absolute))
            {
                _repository.MarkUncachedAsync([wallpaper.Id], cancellationToken).GetAwaiter().GetResult();
                return new CacheRemovalResult { Success = true };
            }

            if (!SafePath.TrySafeDeleteFile(_policy.CacheRoot, absolute))
            {
                return new CacheRemovalResult { Success = false, Error = "File could not be deleted safely." };
            }

            _repository.MarkUncachedAsync([wallpaper.Id], cancellationToken).GetAwaiter().GetResult();
            PruneEmptyParentDirectories(absolute);
            return new CacheRemovalResult { Success = true, FreedBytes = wallpaper.FileSizeBytes };
        }, cancellationToken);
    }

    /// <summary>
    /// Repairs cache metadata after crashes: rows claiming cached files that are
    /// gone become uncached, and orphan files with no database row are removed.
    /// </summary>
    public Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            Directory.CreateDirectory(_policy.CacheRoot);
            var cached = await _repository.GetCachedOrderedByLeastRecentUseAsync(cancellationToken).ConfigureAwait(false);

            var missingIds = new List<Guid>();
            var knownRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var wallpaper in cached)
            {
                knownRelativePaths.Add(wallpaper.CacheRelativePath.Replace('/', Path.DirectorySeparatorChar));
                var absolute = ResolveCachePath(wallpaper.CacheRelativePath);
                if (absolute is null || !File.Exists(absolute))
                {
                    missingIds.Add(wallpaper.Id);
                }
            }

            if (missingIds.Count > 0)
            {
                await _repository.MarkUncachedAsync(missingIds, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Reconcile: {Count} wallpapers marked uncached because their files were missing", missingIds.Count);
            }

            // Orphaned files can only appear under the managed cache root and are
            // removed only when SafePath confirms containment and no reparse points.
            var orphans = new List<string>();
            var info = new DirectoryInfo(_policy.CacheRoot);
            foreach (var file in info.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var relative = Path.GetRelativePath(_policy.CacheRoot, file.FullName);
                if (!knownRelativePaths.Contains(relative))
                {
                    orphans.Add(file.FullName);
                }
            }

            foreach (var orphan in orphans)
            {
                if (SafePath.TrySafeDeleteFile(_policy.CacheRoot, orphan))
                {
                    _logger.LogInformation("Reconcile removed orphaned cache file {File}", Path.GetFileName(orphan));
                }
            }

            if (orphans.Count > 0 || missingIds.Count > 0)
            {
                PruneEmptyParentDirectories(_policy.CacheRoot);
            }
        }, cancellationToken);
    }

    private void PruneEmptyParentDirectories(string fileOrRoot)
    {
        try
        {
            var directory = Path.GetDirectoryName(fileOrRoot);
            while (directory is not null
                && SafePath.IsWithinRoot(_policy.CacheRoot, directory)
                && directory.Length > _policy.CacheRoot.TrimEnd(Path.DirectorySeparatorChar).Length)
            {
                if (Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    break;
                }

                Directory.Delete(directory);
                directory = Path.GetDirectoryName(directory);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        await using var to = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await from.CopyToAsync(to, cancellationToken).ConfigureAwait(false);
    }
}
