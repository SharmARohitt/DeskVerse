namespace Deskverse.Core.Abstractions;

using Deskverse.Core.Entities;

public interface IPreferencesStore
{
    Task<UserPreferences> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default);

    /// <summary>True once the user has completed first-run storage setup.</summary>
    Task<bool> IsFirstRunCompleteAsync(CancellationToken cancellationToken = default);

    Task MarkFirstRunCompleteAsync(CancellationToken cancellationToken = default);
}

public interface IUsageRepository
{
    Task AddAsync(WallpaperUsage usage, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WallpaperUsage>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Closes the most recent open usage record with its duration.</summary>
    Task CloseOpenUsageAsync(Guid wallpaperId, DateTimeOffset endedAtUtc, CancellationToken cancellationToken = default);

    Task SetFeedbackAsync(Guid usageId, Core.UserFeedback feedback, CancellationToken cancellationToken = default);
}

public interface IProviderConfigStore
{
    Task<IReadOnlyList<ProviderConfiguration>> LoadAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ProviderConfiguration configuration, CancellationToken cancellationToken = default);
}

public interface ICollectionRepository
{
    Task<IReadOnlyList<WallpaperCollection>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<WallpaperCollection?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WallpaperCollection> AddAsync(WallpaperCollection collection, CancellationToken cancellationToken = default);

    Task RenameAsync(Guid id, string newName, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddItemAsync(Guid collectionId, Guid wallpaperId, CancellationToken cancellationToken = default);

    Task<bool> RemoveItemAsync(Guid collectionId, Guid wallpaperId, CancellationToken cancellationToken = default);

    Task<bool> ContainsAsync(Guid collectionId, Guid wallpaperId, CancellationToken cancellationToken = default);
}
