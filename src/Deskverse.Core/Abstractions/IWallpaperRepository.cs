namespace Deskverse.Core.Abstractions;

using Deskverse.Core;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;

public sealed record WallpaperPage(IReadOnlyList<Wallpaper> Items, int TotalCount);

/// <summary>Persistence surface for wallpapers. Implementations must be transactional.</summary>
public interface IWallpaperRepository
{
    Task<Wallpaper> AddAsync(Wallpaper wallpaper, CancellationToken cancellationToken = default);

    Task UpdateAsync(Wallpaper wallpaper, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Wallpaper?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Wallpaper?> GetByHashAsync(string fileHash, CancellationToken cancellationToken = default);

    Task<Wallpaper?> GetBySourceAsync(string providerId, string sourceId, CancellationToken cancellationToken = default);

    Task<WallpaperPage> QueryAsync(WallpaperQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Wallpaper>> GetCachedOrderedByLeastRecentUseAsync(CancellationToken cancellationToken = default);

    /// <summary>The wallpaper most recently applied, when one exists.</summary>
    Task<Wallpaper?> GetLastUsedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Wallpaper>> GetRecentlyUsedAsync(int count, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default);

    Task SetDislikedAsync(Guid id, bool isDisliked, CancellationToken cancellationToken = default);

    Task SetPinnedAsync(Guid id, bool isPinned, CancellationToken cancellationToken = default);

    /// <summary>Marks an apply: bumps use counters, last-used stamp and LRU stamp.</summary>
    Task RecordUsageAsync(Guid id, CancellationToken cancellationToken = default);

    Task TouchCacheAccessAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Atomically clears the cached flag for several wallpapers in one transaction.</summary>
    Task MarkUncachedAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
