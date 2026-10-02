namespace Deskverse.App.Messaging;

using CommunityToolkit.Mvvm.Messaging;
using Deskverse.App.ViewModels;

/// <summary>Raised when a single wallpaper's flags or metadata changed.</summary>
public sealed record WallpaperUpdatedMessage(Guid WallpaperId);

/// <summary>Raised when wallpapers were added, removed, or re-cached.</summary>
public sealed record LibraryChangedMessage;

/// <summary>Raised after a wallpaper was applied, stopped, or restored.</summary>
public sealed record EngineStateChangedMessage;

/// <summary>Raised when a wallpaper is picked to inspect/edit in Studio.</summary>
public sealed record WallpaperSelectedMessage(WallpaperItemViewModel Item);

/// <summary>Raised when the display topology changes (monitor connect/disconnect/resize).</summary>
public sealed record DisplayTopologyChangedMessage;
