namespace Deskverse.Core.Abstractions;

using Deskverse.Core.Models;

public interface IDisplayService
{
    IReadOnlyList<DisplayInfo> GetDisplays();

    /// <summary>The primary display, used for single-display decisions.</summary>
    DisplayInfo GetPrimaryDisplay();

    event EventHandler? DisplaysChanged;
}

/// <summary>
/// Win32 surface for setting the desktop wallpaper, abstracted so the static
/// engine logic is unit-testable without touching a real desktop.
/// </summary>
public interface ISystemWallpaperApi
{
    bool SetWallpaper(string absoluteImagePath, WallpaperPlacement placement);

    string? GetWallpaperPath();
}
