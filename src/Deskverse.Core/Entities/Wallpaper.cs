namespace Deskverse.Core.Entities;

using Deskverse.Core;

/// <summary>
/// A managed wallpaper. Cache paths are stored relative to the configured cache
/// root so the cache can be relocated without rewriting every row.
/// </summary>
public class Wallpaper
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Provider identifier, e.g. "local", "mock", or a future remote provider id.</summary>
    public string? SourceProvider { get; set; }

    /// <summary>Stable identifier within the provider.</summary>
    public string? SourceId { get; set; }

    /// <summary>Remote page or API URL where the content came from, when applicable.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>The user's original file location for imports. Never modified or deleted by DeskVerse.</summary>
    public string? OriginalImportPath { get; set; }

    /// <summary>Path relative to the cache root, using forward slashes.</summary>
    public string CacheRelativePath { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the file content, hex encoded. Used for duplicate detection.</summary>
    public string FileHash { get; set; } = string.Empty;

    public WallpaperKind Kind { get; set; }

    public WallpaperFormat Format { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public long FileSizeBytes { get; set; }

    public string? License { get; set; }

    public string? Attribution { get; set; }

    /// <summary>Category tags supplied by the provider or inferred at import time.</summary>
    public string[] Categories { get; set; } = [];

    /// <summary>Dominant color as RRGGBB hex, when analyzed.</summary>
    public string? DominantColor { get; set; }

    /// <summary>Average perceived brightness in [0,1], when analyzed.</summary>
    public double? Brightness { get; set; }

    /// <summary>Edge/visual density estimate in [0,1], when analyzed.</summary>
    public double? VisualDensity { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedAt { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsPinned { get; set; }

    /// <summary>True when the payload file is present in the managed cache.</summary>
    public bool IsCached { get; set; }

    public SafetyStatus SafetyStatus { get; set; } = SafetyStatus.Pending;

    /// <summary>True when the file originated from a local user import.</summary>
    public bool IsUserImported { get; set; }

    /// <summary>Monotonic tick stamp used for LRU ordering. Updated on apply and on cache touch.</summary>
    public long CacheLastAccessTicks { get; set; } = DateTimeOffset.UtcNow.UtcTicks;

    public int UseCount { get; set; }

    public bool IsDisliked { get; set; }

    public ICollection<WallpaperUsage> Usage { get; set; } = new List<WallpaperUsage>();

    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;
}
