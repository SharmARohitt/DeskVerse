namespace Deskverse.App.Services;

using Deskverse.App.Messaging;
using Deskverse.Application;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

/// <summary>
/// Shared wallpaper operations for item-level commands across views. Wraps
/// WallpaperManager calls and broadcasts messenger events so every open view
/// refreshes coherently.
/// </summary>
public sealed class WallpaperActions
{
    private readonly WallpaperManager _manager;
    private readonly IThumbnailService _thumbnails;
    private readonly NotificationService _notifications;
    private readonly IMessenger _messenger;
    private readonly ILogger<WallpaperActions> _logger;

    public WallpaperActions(
        WallpaperManager manager,
        IThumbnailService thumbnails,
        NotificationService notifications,
        ILogger<WallpaperActions> logger)
    {
        _manager = manager;
        _thumbnails = thumbnails;
        _notifications = notifications;
        _logger = logger;
        _messenger = WeakReferenceMessenger.Default;
    }

    public WallpaperManager Manager => _manager;

    public async Task ApplyAsync(Guid id, string? displayDeviceName = null)
    {
        var outcome = await _manager.ApplyAsync(id, displayDeviceName).ConfigureAwait(true);
        if (outcome.Success)
        {
            _notifications.Success(outcome.Notice ?? "Wallpaper applied.");
        }
        else
        {
            _notifications.Error(outcome.Error ?? "The wallpaper could not be applied.");
        }

        _messenger.Send(new EngineStateChangedMessage());
    }

    public async Task SetFavoriteAsync(Guid id, bool isFavorite)
    {
        await _manager.SetFavoriteAsync(id, isFavorite).ConfigureAwait(true);
        _messenger.Send(new WallpaperUpdatedMessage(id));
    }

    public async Task SetPinnedAsync(Guid id, bool isPinned)
    {
        await _manager.SetPinnedAsync(id, isPinned).ConfigureAwait(true);
        _notifications.Info(isPinned ? "Pinned — the cache will always keep this wallpaper." : "Unpinned.");
        _messenger.Send(new WallpaperUpdatedMessage(id));
    }

    public async Task SetDislikedAsync(Guid id, bool isDisliked)
    {
        await _manager.SetDislikedAsync(id, isDisliked).ConfigureAwait(true);
        if (isDisliked)
        {
            _notifications.Info("Marked as disliked. It will not be recommended or auto-selected again.");
        }

        _messenger.Send(new WallpaperUpdatedMessage(id));
    }

    public async Task RemoveFromCacheAsync(Guid id)
    {
        var result = await _manager.RemoveFromCacheAsync(id).ConfigureAwait(true);
        if (result.Success)
        {
            await _thumbnails.InvalidateAsync(id).ConfigureAwait(true);
            _notifications.Info("Removed from the cache. The original file is never touched.");
            _messenger.Send(new WallpaperUpdatedMessage(id));
        }
        else
        {
            _notifications.Error(result.Error ?? "The wallpaper could not be removed from the cache.");
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        var result = await _manager.DeleteAsync(id).ConfigureAwait(true);
        if (result.Success)
        {
            await _thumbnails.InvalidateAsync(id).ConfigureAwait(true);
            _notifications.Info("Deleted from the DeskVerse library.");
            _messenger.Send(new LibraryChangedMessage());
        }
        else
        {
            _notifications.Error(result.Error ?? "The wallpaper could not be deleted.");
        }
    }

    public async Task ImportAsync(IReadOnlyList<string> paths)
    {
        var imported = 0;
        foreach (var path in paths)
        {
            var outcome = await _manager.ImportAsync(path).ConfigureAwait(true);
            if (outcome.Success)
            {
                imported++;
            }
            else
            {
                _notifications.Warning(outcome.Detail ?? outcome.Message ?? $"'{path}' was rejected.");
            }
        }

        if (imported > 0)
        {
            _notifications.Success($"Imported {imported} wallpaper{(imported == 1 ? string.Empty : "s")}.");
            _messenger.Send(new LibraryChangedMessage());
        }
    }

    public async Task RefreshThumbnailAsync(Wallpaper wallpaper)
    {
        await _thumbnails.InvalidateAsync(wallpaper.Id).ConfigureAwait(true);
        _ = await _thumbnails.GetOrCreateThumbnailAsync(wallpaper).ConfigureAwait(true);
        _messenger.Send(new WallpaperUpdatedMessage(wallpaper.Id));
    }

    public async Task<EngineStatus> GetEngineStatusAsync()
    {
        return await _manager.GetEngineStatusAsync().ConfigureAwait(true);
    }

    public void LogError(string context, Exception ex) =>
        _logger.LogError(ex, "{Context}", context);
}
