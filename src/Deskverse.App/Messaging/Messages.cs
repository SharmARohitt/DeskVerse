namespace Deskverse.App.Messaging;

using CommunityToolkit.Mvvm.Messaging;

/// <summary>Raised when a single wallpaper's flags or metadata changed.</summary>
public sealed record WallpaperUpdatedMessage(Guid WallpaperId);

/// <summary>Raised when wallpapers were added, removed, or re-cached.</summary>
public sealed record LibraryChangedMessage;

/// <summary>Raised after a wallpaper was applied, stopped, or restored.</summary>
public sealed record EngineStateChangedMessage;

/// <summary>Raised when the user picks a wallpaper to inspect or edit in Studio.</summary>
public sealed record WallpaperSelectedMessage(WallpaperItemViewModel Item);
