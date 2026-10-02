namespace Deskverse.Core.Models;

using Deskverse.Core;

/// <summary>Engine status snapshot for UI and API consumers.</summary>
public sealed record EngineStatus(
    EngineState State,
    WallpaperKind? ActiveKind,
    Guid? ActiveWallpaperId,
    string? ActiveWallpaperTitle,
    DateTimeOffset? StartedAt,
    string? LastError);

/// <summary>What a wallpaper engine implementation can actually do on this machine.</summary>
public sealed record EngineCapabilities(
    bool SupportsStatic,
    bool SupportsVideo,
    bool SupportsMultiDisplay,
    IReadOnlyList<string> KnownLimitations);

/// <summary>Request to make a wallpaper active. The caller resolves cache paths.</summary>
public sealed record ApplyRequest(
    Guid WallpaperId,
    WallpaperKind Kind,
    string Title,
    string AbsolutePath,
    WallpaperPlacement Placement = WallpaperPlacement.Fill,
    string? DisplayDeviceName = null)
{
    /// <summary>Display to target; null means all displays / primary.</summary>
    public bool TargetsAllDisplays => DisplayDeviceName is null;
}
