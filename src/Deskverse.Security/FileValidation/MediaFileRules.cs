namespace Deskverse.Security.FileValidation;

using Deskverse.Core;

/// <summary>Extension and format policy mapping for supported wallpaper media.</summary>
public static class MediaFileRules
{
    private static readonly Dictionary<string, WallpaperFormat> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = WallpaperFormat.Jpeg,
        [".jpeg"] = WallpaperFormat.Jpeg,
        [".png"] = WallpaperFormat.Png,
        [".bmp"] = WallpaperFormat.Bmp,
        [".gif"] = WallpaperFormat.Gif,
        [".webp"] = WallpaperFormat.WebP,
    };

    private static readonly Dictionary<string, WallpaperFormat> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = WallpaperFormat.Mp4,
        [".m4v"] = WallpaperFormat.Mp4,
        [".webm"] = WallpaperFormat.WebM,
        [".mkv"] = WallpaperFormat.Matroska,
        [".mov"] = WallpaperFormat.QuickTimeMov,
        [".avi"] = WallpaperFormat.Avi,
    };

    /// <summary>Extensions that may be executed or interpreted. Never accepted as wallpaper media.</summary>
    public static readonly IReadOnlySet<string> BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".msp", ".mst", ".scr", ".cpl", ".jar", ".com", ".pif", ".gadget", ".hta", ".lnk",
        ".sh", ".bash", ".py", ".pyw", ".pl", ".rb", ".reg", ".inf", ".ade", ".adp", ".app", ".msp",
    };

    public static bool TryGetFormat(string extension, out WallpaperFormat format, out WallpaperKind kind)
    {
        format = WallpaperFormat.Unknown;
        kind = WallpaperKind.Static;
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        var normalized = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        if (ImageExtensions.TryGetValue(normalized, out var imageFormat))
        {
            format = imageFormat;
            kind = WallpaperKind.Static;
            return true;
        }

        if (VideoExtensions.TryGetValue(normalized, out var videoFormat))
        {
            format = videoFormat;
            kind = WallpaperKind.Video;
            return true;
        }

        return false;
    }

    public static WallpaperKind KindForFormat(WallpaperFormat format) => format switch
    {
        WallpaperFormat.Jpeg or WallpaperFormat.Png or WallpaperFormat.Bmp
            or WallpaperFormat.Gif or WallpaperFormat.WebP => WallpaperKind.Static,
        WallpaperFormat.Mp4 or WallpaperFormat.WebM or WallpaperFormat.Matroska
            or WallpaperFormat.QuickTimeMov or WallpaperFormat.Avi => WallpaperKind.Video,
        _ => WallpaperKind.Static,
    };

    public static bool IsBlockedExtension(string extension) =>
        !string.IsNullOrWhiteSpace(extension) && BlockedExtensions.Contains(extension.ToLowerInvariant());
}
