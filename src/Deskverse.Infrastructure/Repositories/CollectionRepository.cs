namespace Deskverse.Infrastructure.Repositories;

using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class CollectionRepository : ICollectionRepository
{
    private readonly IDbContextFactory<DeskverseDbContext> _contextFactory;

    public CollectionRepository(IDbContextFactory<DeskverseDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<WallpaperCollection>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Collections
            .AsNoTracking()
            .Include(c => c.Items!)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<WallpaperCollection?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Collections
            .AsNoTracking()
            .Include(c => c.Items!)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<WallpaperCollection> AddAsync(
        WallpaperCollection collection,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        db.Collections.Add(collection);
        await db.SaveChangesAsync(cancellationToken);
        return collection;
    }

    public async Task RenameAsync(Guid id, string newName, CancellationToken cancellationToken = default)
    {
        var name = newName.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Collection name cannot be empty.", nameof(newName));
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Collections
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.Name, name),
                cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // System collections (e.g. Favorites) are protected from deletion.
        var isSystem = await db.Collections
            .Where(c => c.Id == id)
            .Select(c => c.IsSystem)
            .FirstOrDefaultAsync(cancellationToken);
        if (isSystem)
        {
            return false;
        }

        var deleted = await db.Collections
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task AddItemAsync(
        Guid collectionId,
        Guid wallpaperId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await db.CollectionItems
            .AnyAsync(i => i.CollectionId == collectionId && i.WallpaperId == wallpaperId, cancellationToken);
        if (exists)
        {
            return;
        }

        db.CollectionItems.Add(new CollectionItem
        {
            CollectionId = collectionId,
            WallpaperId = wallpaperId,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveItemAsync(
        Guid collectionId,
        Guid wallpaperId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.CollectionItems
            .Where(i => i.CollectionId == collectionId && i.WallpaperId == wallpaperId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task<bool> ContainsAsync(
        Guid collectionId,
        Guid wallpaperId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CollectionItems
            .AnyAsync(i => i.CollectionId == collectionId && i.WallpaperId == wallpaperId, cancellationToken);
    }
}
