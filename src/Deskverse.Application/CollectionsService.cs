namespace Deskverse.Application;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// User collections: create, rename, delete, and membership. Deletion of system
/// collections is refused by the repository; this layer adds validation only.
/// </summary>
public sealed class CollectionsService
{
    private readonly ICollectionRepository _collections;
    private readonly IWallpaperRepository _wallpapers;
    private readonly ILogger<CollectionsService> _logger;

    public CollectionsService(
        ICollectionRepository collections,
        IWallpaperRepository wallpapers,
        ILogger<CollectionsService> logger)
    {
        _collections = collections;
        _wallpapers = wallpapers;
        _logger = logger;
    }

    public Task<IReadOnlyList<WallpaperCollection>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _collections.GetAllAsync(cancellationToken);

    public Task<WallpaperCollection?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _collections.GetAsync(id, cancellationToken);

    public async Task<OperationResult<WallpaperCollection>> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > 60)
        {
            return OperationResult<WallpaperCollection>.Fail("Collection names must be 1-60 characters.");
        }

        var existing = await _collections.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<WallpaperCollection>.Fail($"A collection named '{trimmed}' already exists.");
        }

        var collection = await _collections
            .AddAsync(new WallpaperCollection { Name = trimmed }, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation("Created collection {Id} '{Name}'.", collection.Id, collection.Name);
        return OperationResult<WallpaperCollection>.Ok(collection);
    }

    public Task RenameAsync(Guid id, string newName, CancellationToken cancellationToken = default)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (trimmed.Length is < 1 or > 60)
        {
            throw new ArgumentException("Collection names must be 1-60 characters.", nameof(newName));
        }

        return _collections.RenameAsync(id, trimmed, cancellationToken);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _collections.DeleteAsync(id, cancellationToken);

    public async Task<OperationResult> AddWallpaperAsync(
        Guid collectionId,
        Guid wallpaperId,
        CancellationToken cancellationToken = default)
    {
        var wallpaper = await _wallpapers.GetByIdAsync(wallpaperId, cancellationToken).ConfigureAwait(false);
        if (wallpaper is null)
        {
            return OperationResult.Fail($"No wallpaper with id {wallpaperId} exists.");
        }

        var collection = await _collections.GetAsync(collectionId, cancellationToken).ConfigureAwait(false);
        if (collection is null)
        {
            return OperationResult.Fail($"No collection with id {collectionId} exists.");
        }

        await _collections.AddItemAsync(collectionId, wallpaperId, cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok();
    }

    public Task<bool> RemoveWallpaperAsync(
        Guid collectionId,
        Guid wallpaperId,
        CancellationToken cancellationToken = default) =>
        _collections.RemoveItemAsync(collectionId, wallpaperId, cancellationToken);
}
