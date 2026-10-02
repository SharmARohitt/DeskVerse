namespace Deskverse.Core.Models;

using Deskverse.Core;

/// <summary>Shared query shape for the library and discovery filters.</summary>
public sealed record WallpaperQuery
{
    public string? SearchText { get; init; }

    public string[] Categories { get; init; } = [];

    public WallpaperKind? Kind { get; init; }

    public int? MinWidth { get; init; }

    public int? MinHeight { get; init; }

    /// <summary>Aspect ratio within the given tolerance (e.g. 1.78 ± 0.02 for 16:9).</summary>
    public double? AspectRatio { get; init; }

    public double AspectTolerance { get; init; } = 0.02;

    /// <summary>Dominant color family to match, as RRGGBB hex.</summary>
    public string? ColorHex { get; init; }

    /// <summary>Hue bucket tolerance when matching colors (0..1).</summary>
    public double ColorTolerance { get; init; } = 0.12;

    public double? MinBrightness { get; init; }

    public double? MaxBrightness { get; init; }

    public bool FavoritesOnly { get; init; }

    public bool PinnedOnly { get; init; }

    public bool CachedOnly { get; init; }

    public bool UserImportedOnly { get; init; }

    public bool ExcludeDisliked { get; init; } = true;

    public SortOrder SortBy { get; init; } = SortOrder.NewestFirst;

    public int Skip { get; init; }

    public int Take { get; init; } = 60;

    public WallpaperQuery WithPaging(int skip, int take) => this with { Skip = skip, Take = take };
}
