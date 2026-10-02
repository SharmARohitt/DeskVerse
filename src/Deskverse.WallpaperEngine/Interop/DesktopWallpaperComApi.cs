namespace Deskverse.WallpaperEngine.Interop;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Deskverse.Core;
using Deskverse.Core.Abstractions;

/// <summary>
/// Modern per-monitor wallpaper API (Windows 8+). Supports targeting a single
/// display and native placement mapping, which SystemParametersInfo cannot do.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DesktopWallpaperComApi : ISystemWallpaperApi
{
    private const int DwposCenter = 0;

    private const int DwposTile = 1;

    private const int DwposStretch = 2;

    private const int DwposFit = 3;

    private const int DwposFill = 4;

    private const int DwposSpan = 5;

    public bool SetWallpaper(string absoluteImagePath, WallpaperPlacement placement)
    {
        if (string.IsNullOrWhiteSpace(absoluteImagePath))
        {
            return false;
        }

        try
        {
            var wallpaper = (IDesktopWallpaper)(object)new DesktopWallpaperComObject();
            wallpaper.SetPosition(placement switch
            {
                WallpaperPlacement.Tile => DwposTile,
                WallpaperPlacement.Center => DwposCenter,
                WallpaperPlacement.Stretch => DwposStretch,
                WallpaperPlacement.Fit => DwposFit,
                WallpaperPlacement.Span => DwposSpan,
                _ => DwposFill,
            });

            // Passing null applies the image to every monitor.
            wallpaper.SetWallpaper(null, absoluteImagePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Applies the image to one monitor by its device path (\\.\DISPLAY1 etc.).</summary>
    public bool SetWallpaperForMonitor(string monitorDevicePath, string absoluteImagePath, WallpaperPlacement placement)
    {
        if (string.IsNullOrWhiteSpace(absoluteImagePath) || string.IsNullOrWhiteSpace(monitorDevicePath))
        {
            return false;
        }

        try
        {
            var wallpaper = (IDesktopWallpaper)(object)new DesktopWallpaperComObject();
            wallpaper.SetPosition(placement switch
            {
                WallpaperPlacement.Tile => DwposTile,
                WallpaperPlacement.Center => DwposCenter,
                WallpaperPlacement.Stretch => DwposStretch,
                WallpaperPlacement.Fit => DwposFit,
                WallpaperPlacement.Span => DwposSpan,
                _ => DwposFill,
            });
            wallpaper.SetWallpaper(monitorDevicePath, absoluteImagePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string? GetWallpaperPath()
    {
        try
        {
            var wallpaper = (IDesktopWallpaper)(object)new DesktopWallpaperComObject();
            var path = wallpaper.GetWallpaper(null);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Monitor device paths in enumeration order (\\.\DISPLAY1, ...).</summary>
    public IReadOnlyList<string> GetMonitorDevicePaths()
    {
        try
        {
            var wallpaper = (IDesktopWallpaper)(object)new DesktopWallpaperComObject();
            var count = wallpaper.GetMonitorDevicePathCount();
            var paths = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                paths.Add(wallpaper.GetMonitorDevicePathAt(i));
            }

            return paths;
        }
        catch
        {
            return [];
        }
    }

    [ComImport]
    [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private sealed class DesktopWallpaperComObject
    {
    }

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
            [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);

        uint GetMonitorDevicePathCount();

        void GetMonitorRECT(
            [MarshalAs(UnmanagedType.LPWStr)] string monitorId,
            out RECT displayRect);

        void SetBackgroundColor(uint color);

        uint GetBackgroundColor();

        void SetPosition(int position);

        int GetPosition();

        void SetSlideshow(IntPtr slideshow);

        IntPtr GetSlideshow();

        void SetSlideshowOptions(uint options, uint slideshowTick);

        void GetSlideshowOptions(out uint options, out uint slideshowTick);

        void AdvanceSlideshow(
            [MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
            int direction);

        int GetStatus();

        void Enable(int enable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }
}
