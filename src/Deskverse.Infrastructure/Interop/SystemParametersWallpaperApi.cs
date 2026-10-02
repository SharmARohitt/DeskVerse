namespace Deskverse.Infrastructure.Interop;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Deskverse.Core;
using Deskverse.Core.Abstractions;

/// <summary>
/// Sets the desktop wallpaper through SystemParametersInfo, mapping our placement
/// enum onto the registry TileWallpaper/WallpaperStyle values Windows honors.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemParametersWallpaperApi : ISystemWallpaperApi
{
    private const uint SpiGetDeskWallpaper = 0x0074;

    private const uint SpiSetDeskWallpaper = 0x0014;

    private const uint SpiFUpdateIniFile = 0x01;

    private const uint SpiFSendChange = 0x02;

    public bool SetWallpaper(string absoluteImagePath, WallpaperPlacement placement)
    {
        if (string.IsNullOrWhiteSpace(absoluteImagePath))
        {
            return false;
        }

        // TileWallpaper: 1 = tile, 0 = everything else.
        // WallpaperStyle: 0 = center/tile, 2 = stretch, 6 = fill, 10 = fit, 22 = span.
        var (tile, style) = placement switch
        {
            WallpaperPlacement.Tile => (1, 0),
            WallpaperPlacement.Center => (0, 0),
            WallpaperPlacement.Stretch => (0, 2),
            WallpaperPlacement.Fit => (0, 10),
            WallpaperPlacement.Span => (0, 22),
            _ => (0, 6), // Fill is the DeskVerse default.
        };

        using var tileKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
        tileKey?.SetValue("TileWallpaper", tile.ToString(), Microsoft.Win32.RegistryValueKind.String);
        tileKey?.SetValue("WallpaperStyle", style.ToString(), Microsoft.Win32.RegistryValueKind.String);

        return SystemParametersInfo(
            SpiSetDeskWallpaper,
            0,
            new StringBuilder(absoluteImagePath),
            SpiFUpdateIniFile | SpiFSendChange);
    }

    public string? GetWallpaperPath()
    {
        var buffer = new StringBuilder(260);
        if (SystemParametersInfo(SpiGetDeskWallpaper, (uint)buffer.Capacity, buffer, 0))
        {
            var value = buffer.ToString();
            return value.Length > 0 ? value : null;
        }

        return null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint param,
        StringBuilder vParam,
        uint flags);
}
