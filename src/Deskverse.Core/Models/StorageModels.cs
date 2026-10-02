namespace Deskverse.Core.Models;

using Deskverse.Core;

/// <summary>Cache and disk accounting snapshot.</summary>
public sealed record StorageStatus(
    string CacheDirectory,
    long LimitBytes,
    long UsedBytes,
    long ThumbnailBytes,
    long AvailableDiskBytes,
    long ActiveWallpaperBytes,
    long PinnedBytes,
    int CachedEntryCount,
    StorageHealth Health,
    string HealthMessage);
