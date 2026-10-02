namespace Deskverse.Core.Abstractions;

using Deskverse.Core.Entities;

/// <summary>
/// Generates and caches downscaled PNG thumbnails for wallpapers. Must not run
/// expensive decoding on the UI thread; implementations own their scheduling.
/// </summary>
public interface IThumbnailService
{
    /// <summary>Returns an absolute path to a cached thumbnail, creating it when missing.</summary>
    Task<string?> GetOrCreateThumbnailAsync(Wallpaper wallpaper, CancellationToken cancellationToken = default);

    Task InvalidateAsync(Guid wallpaperId, CancellationToken cancellationToken = default);
}

/// <summary>Extracts visual features (dominant color, brightness, density) from media files.</summary>
public interface IVisualAnalyzer
{
    Task<VisualFeatures?> AnalyzeAsync(string absolutePath, CancellationToken cancellationToken = default);
}

public sealed record VisualFeatures(string DominantColorHex, double Brightness, double VisualDensity, int Width, int Height);
