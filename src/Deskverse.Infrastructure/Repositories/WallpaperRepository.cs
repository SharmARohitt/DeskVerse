namespace Deskverse.Infrastructure.Repositories;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Deskverse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// SQLite-backed repository. Coarse filters translate to SQL; aspect-ratio,
/// color-similarity, and random ordering run in memory over a bounded candidate
/// set because SQLite cannot express them.
/// </summary>
public sealed class WallpaperRepository : IWallpaperRepository
{
    private const int MaxPageSize = 240;

    /// <summary>Upper bound for in-memory refinement passes so the UI never stalls on huge libraries.</summary>
    private const int InMemoryCap = 5000;

    private readonly IDbContextFactory<DeskverseDbContext> _contextFactory;

    public WallpaperRepository(IDbContextFactory<DeskverseDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Wallpaper> AddAsync(Wallpaper wallpaper, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        db.Wallpapers.Add(wallpaper);
        await db.SaveChangesAsync(cancellationToken);
        return wallpaper;
    }

    public async Task UpdateAsync(Wallpaper wallpaper, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Wallpapers.FirstOrDefaultAsync(w => w.Id == wallpaper.Id, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException($"Wallpaper {wallpaper.Id} no longer exists.");
        }

        db.Entry(existing).CurrentValues.SetValues(wallpaper);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.Wallpapers
            .Where(w => w.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task<Wallpaper?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Wallpapers
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<Wallpaper?> GetByHashAsync(string fileHash, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Wallpapers
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.FileHash == fileHash, cancellationToken);
    }

    public async Task<Wallpaper?> GetBySourceAsync(
        string providerId,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Wallpapers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                w => w.SourceProvider == providerId && w.SourceId == sourceId,
                cancellationToken);
    }

    public async Task<WallpaperPage> QueryAsync(WallpaperQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var coarse = ApplyCoarseFilters(db.Wallpapers.AsNoTracking(), query);

        var needsMemoryPass = query.AspectRatio is not null
            || query.ColorHex is not null
            || query.SortBy == SortOrder.Random;

        if (!needsMemoryPass)
        {
            var total = await coarse.CountAsync(cancellationToken);
            var take = Math.Clamp(query.Take, 0, MaxPageSize);
            var items = await ApplySort(coarse, query.SortBy)
                .Skip(Math.Max(query.Skip, 0))
                .Take(take)
                .ToListAsync(cancellationToken);
            return new WallpaperPage(items, total);
        }

        var candidates = await ApplySort(coarse, SortOrder.NewestFirst)
            .Take(InMemoryCap)
            .ToListAsync(cancellationToken);

        IEnumerable<Wallpaper> refined = candidates;
        if (query.AspectRatio is { } target)
        {
            refined = refined.Where(w => w.Height > 0 && Math.Abs(w.AspectRatio - target) <= query.AspectTolerance);
        }

        if (query.ColorHex is { } colorHex)
        {
            refined = refined.Where(w =>
                w.DominantColor is not null
                && ColorMath.ColorDistance(w.DominantColor, colorHex) <= query.ColorTolerance);
        }

        if (query.SortBy == SortOrder.Random)
        {
            refined = refined.OrderBy(_ => Guid.NewGuid());
        }

        var list = refined.ToList();
        var page = list
            .Skip(Math.Max(query.Skip, 0))
            .Take(Math.Clamp(query.Take, 0, MaxPageSize))
            .ToList();
        return new WallpaperPage(page, list.Count);
    }

    public async Task<IReadOnlyList<Wallpaper>> GetCachedOrderedByLeastRecentUseAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Wallpapers
            .AsNoTracking()
            .Where(w => w.IsCached)
            .OrderBy(w => w.CacheLastAccessTicks)
            .ToListAsync(cancellationToken);
    }

    public async Task<Wallpaper?> GetLastUsedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Wallpapers
            .AsNoTracking()
            .Where(w => w.LastUsedAt != null)
            .OrderByDescending(w => w.LastUsedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Wallpaper>> GetRecentlyUsedAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Wallpapers
            .AsNoTracking()
            .Where(w => w.LastUsedAt != null)
            .OrderByDescending(w => w.LastUsedAt)
            .Take(Math.Clamp(count, 1, 100))
            .ToListAsync(cancellationToken);
    }

    public async Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Wallpapers
            .Where(w => w.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.IsFavorite, isFavorite),
                cancellationToken);
    }

    public async Task SetDislikedAsync(Guid id, bool isDisliked, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Wallpapers
            .Where(w => w.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.IsDisliked, isDisliked),
                cancellationToken);
    }

    public async Task SetPinnedAsync(Guid id, bool isPinned, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Wallpapers
            .Where(w => w.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.IsPinned, isPinned),
                cancellationToken);
    }

    public async Task RecordUsageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var affected = await db.Wallpapers
            .Where(w => w.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.UseCount, w => w.UseCount + 1)
                    .SetProperty(w => w.LastUsedAt, now)
                    .SetProperty(w => w.CacheLastAccessTicks, now.UtcTicks),
                cancellationToken);

        if (affected > 0)
        {
            db.WallpaperUsage.Add(new WallpaperUsage
            {
                WallpaperId = id,
                AppliedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task TouchCacheAccessAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Wallpapers
            .Where(w => w.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.CacheLastAccessTicks, DateTimeOffset.UtcNow.UtcTicks),
                cancellationToken);
    }

    public async Task MarkUncachedAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Wallpapers
            .Where(w => idList.Contains(w.Id))
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.IsCached, false)
                    .SetProperty(w => w.CacheRelativePath, string.Empty),
                cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Wallpapers.CountAsync(cancellationToken);
    }

    private static IQueryable<Wallpaper> ApplyCoarseFilters(IQueryable<Wallpaper> source, WallpaperQuery query)
    {
        var q = source;

        if (query.Kind is { } kind)
        {
            q = q.Where(w => w.Kind == kind);
        }

        if (query.MinWidth is { } minWidth)
        {
            q = q.Where(w => w.Width >= minWidth);
        }

        if (query.MinHeight is { } minHeight)
        {
            q = q.Where(w => w.Height >= minHeight);
        }

        if (query.FavoritesOnly)
        {
            q = q.Where(w => w.IsFavorite);
        }

        if (query.PinnedOnly)
        {
            q = q.Where(w => w.IsPinned);
        }

        if (query.CachedOnly)
        {
            q = q.Where(w => w.IsCached);
        }

        if (query.UserImportedOnly)
        {
            q = q.Where(w => w.IsUserImported);
        }

        if (query.ExcludeDisliked)
        {
            q = q.Where(w => !w.IsDisliked);
        }

        if (query.MinBrightness is { } minBrightness)
        {
            q = q.Where(w => w.Brightness >= minBrightness);
        }

        if (query.MaxBrightness is { } maxBrightness)
        {
            q = q.Where(w => w.Brightness <= maxBrightness);
        }

        if (query.Categories.Length > 0)
        {
            var wanted = query.Categories.ToList();
            q = q.Where(w => w.Categories.Any(c => wanted.Contains(c)));
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var needle = query.SearchText.Trim();
            q = q.Where(w =>
                EF.Functions.Like(w.Title, $"%{needle}%")
                || (w.Description != null && EF.Functions.Like(w.Description, $"%{needle}%"))
                || w.Categories.Any(c => EF.Functions.Like(c, $"%{needle}%")));
        }

        return q;
    }

    private static IQueryable<Wallpaper> ApplySort(IQueryable<Wallpaper> source, SortOrder order) => order switch
    {
        SortOrder.RecentlyUsed => source
            .OrderByDescending(w => w.LastUsedAt)
            .ThenByDescending(w => w.CreatedAt),
        SortOrder.MostUsedFirst => source
            .OrderByDescending(w => w.UseCount)
            .ThenByDescending(w => w.LastUsedAt),
        SortOrder.TitleAZ => source.OrderBy(w => w.Title).ThenBy(w => w.CreatedAt),
        SortOrder.LargestFirst => source.OrderByDescending(w => w.FileSizeBytes),
        _ => source.OrderByDescending(w => w.CreatedAt).ThenBy(w => w.Id),
    };
}
