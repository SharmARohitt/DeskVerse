namespace Deskverse.Core.Models;

using Deskverse.Core.Entities;

/// <summary>Wallpaper payload after import or provider download, before persistence.</summary>
public sealed record WallpaperPayload(
    Guid Id,
    string Title,
    string? Description,
    string? SourceProvider,
    string? SourceId,
    string? SourceUrl,
    string? OriginalImportPath,
    string CacheRelativePath,
    string FileHash,
    WallpaperKind Kind,
    WallpaperFormat Format,
    int Width,
    int Height,
    long FileSizeBytes,
    string? License,
    string? Attribution,
    string[] Categories,
    string? DominantColor,
    double? Brightness,
    double? VisualDensity,
    bool IsUserImported);
