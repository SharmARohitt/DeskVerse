namespace Deskverse.Providers;

using System.Globalization;
using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;

public enum MockProviderFailure
{
    None = 0,
    Error = 1,
    Timeout = 2,
    Offline = 3,
}

/// <summary>
/// Deterministic development provider. The catalog is generated from a fixed seed
/// so tests and screenshots are reproducible; it never touches the network. The
/// failure mode is switchable so provider-failure handling can be exercised.
/// </summary>
public sealed class MockWallpaperProvider : IWallpaperProvider
{
    public const string ProviderName = "mock";

    private const int CatalogSize = 48;

    private static readonly string[] CategoryPool =
    [
        "Nature", "Abstract", "Cityscape", "Minimal", "Space", "Anime", "Cyberpunk", "Landscape",
    ];

    private static readonly string[] TitlePrefixes =
    [
        "Aurora", "Obsidian", "Nebula", "Frostline", "Ember", "Tidebreak", "Skyward", "Granite",
    ];

    private static readonly string[] TitleSuffixes =
    [
        "Drift", "Ridge", "Bloom", "Horizon", "Echo", "Valley", "Crest", "Mirage",
    ];

    private static readonly (int Width, int Height)[] Sizes =
    [
        (3840, 2160), (2560, 1440), (1920, 1080), (2560, 1600), (3840, 2400),
    ];

    private static readonly string[] Licenses =
    [
        "CC BY 4.0", "CC0 1.0", "Mock Preview License",
    ];

    private readonly ILogger<MockWallpaperProvider> _logger;
    private readonly List<ProviderWallpaper> _catalog;

    public MockWallpaperProvider(ILogger<MockWallpaperProvider> logger)
    {
        _logger = logger;
        _catalog = BuildCatalog();
    }

    /// <summary>Switched by tests to exercise failure handling; never changed by the app in normal use.</summary>
    public MockProviderFailure FailureMode { get; set; }

    public string ProviderId => ProviderName;

    public string DisplayName => "Mock Catalog (Dev)";

    public bool RequiresNetwork => false;

    public Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> SearchAsync(
        WallpaperQuery query,
        CancellationToken cancellationToken = default)
    {
        if (TryFail<IReadOnlyList<ProviderWallpaper>>(cancellationToken, out var failure))
        {
            return Task.FromResult(failure);
        }

        IEnumerable<ProviderWallpaper> items = _catalog;
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var needle = query.SearchText.Trim();
            items = items.Where(w =>
                w.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || w.Categories.Any(c => c.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        if (query.Kind is { } kind)
        {
            items = items.Where(w => w.Kind == kind);
        }

        if (query.MinWidth is { } minWidth)
        {
            items = items.Where(w => w.Width >= minWidth);
        }

        if (query.MinHeight is { } minHeight)
        {
            items = items.Where(w => w.Height >= minHeight);
        }

        if (query.Categories.Length > 0)
        {
            items = items.Where(w => w.Categories.Any(c => query.Categories.Contains(c, StringComparer.OrdinalIgnoreCase)));
        }

        var result = items
            .Skip(query.Skip)
            .Take(Math.Clamp(query.Take, 0, CatalogSize))
            .ToList();
        return Task.FromResult(ProviderResult<IReadOnlyList<ProviderWallpaper>>.Ok(result));
    }

    public Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> GetTrendingAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        if (TryFail<IReadOnlyList<ProviderWallpaper>>(cancellationToken, out var failure))
        {
            return Task.FromResult(failure);
        }

        // Trending = highest mock "popularity" field encoded in FileSizeBytes ordering.
        var result = _catalog
            .OrderByDescending(w => w.FileSizeBytes)
            .ThenBy(w => w.Title, StringComparer.Ordinal)
            .Take(Math.Clamp(count, 1, CatalogSize))
            .ToList();
        return Task.FromResult(ProviderResult<IReadOnlyList<ProviderWallpaper>>.Ok(result));
    }

    public Task<ProviderResult<ProviderWallpaper>> GetDetailsAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (TryFail<ProviderWallpaper>(cancellationToken, out var failure))
        {
            return Task.FromResult(ProviderResult<ProviderWallpaper>.Fail(failure.Error ?? "Provider failed."));
        }

        var item = _catalog.FirstOrDefault(w => w.SourceId == sourceId);
        return Task.FromResult(item is null
            ? ProviderResult<ProviderWallpaper>.Fail($"No mock item with id {sourceId}.")
            : ProviderResult<ProviderWallpaper>.Ok(item));
    }

    public Task<ProviderResult<IReadOnlyList<string>>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        if (TryFail<IReadOnlyList<string>>(cancellationToken, out var failure))
        {
            return Task.FromResult(failure);
        }

        return Task.FromResult(ProviderResult<IReadOnlyList<string>>.Ok(
            CategoryPool.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList()));
    }

    public Task<ProviderResult<ProviderAttribution>> GetAttributionAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (TryFail<ProviderAttribution>(cancellationToken, out var failure))
        {
            return Task.FromResult(ProviderResult<ProviderAttribution>.Fail(failure.Error ?? "Provider failed."));
        }

        var details = _catalog.FirstOrDefault(w => w.SourceId == sourceId);
        if (details is null)
        {
            return Task.FromResult(ProviderResult<ProviderAttribution>.Fail(
                $"No mock item with id {sourceId}."));
        }

        return Task.FromResult(ProviderResult<ProviderAttribution>.Ok(new ProviderAttribution(
            ProviderId,
            details.SourceId,
            details.License,
            details.Attribution,
            details.PageUrl,
            "Mock content generated locally for development. Not real wallpaper data.")));
    }

    private bool TryFail<T>(CancellationToken cancellationToken, out ProviderResult<T> failure)
    {
        switch (FailureMode)
        {
            case MockProviderFailure.Error:
                failure = ProviderResult<T>.Fail("Mock provider simulated an error.");
                return true;
            case MockProviderFailure.Offline:
                failure = ProviderResult<T>.Fail("Mock provider is offline.");
                return true;
            case MockProviderFailure.Timeout:
                _logger.LogWarning("Mock provider simulating a timeout.");
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                failure = ProviderResult<T>.Fail("Mock provider timed out.", timedOut: true);
                return true;
            default:
                failure = null!;
                return false;
        }
    }

    private static List<ProviderWallpaper> BuildCatalog()
    {
        var random = new Random(0xDE5B); // fixed seed: deterministic catalog
        var catalog = new List<ProviderWallpaper>(CatalogSize);

        for (var i = 0; i < CatalogSize; i++)
        {
            var (width, height) = Sizes[random.Next(Sizes.Length)];
            var isVideo = random.Next(4) == 0; // ~25% video
            var kind = isVideo ? WallpaperKind.Video : WallpaperKind.Static;
            var format = isVideo ? WallpaperFormat.Mp4 : random.Next(3) switch
            {
                0 => WallpaperFormat.Jpeg,
                1 => WallpaperFormat.Png,
                _ => WallpaperFormat.WebP,
            };

            var categoryCount = 1 + random.Next(2);
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (categories.Count < categoryCount)
            {
                categories.Add(CategoryPool[random.Next(CategoryPool.Length)]);
            }

            var dominant = ((random.Next(0x100) << 16) | (random.Next(0x100) << 8) | random.Next(0x100))
                .ToString("X6", CultureInfo.InvariantCulture);
            var title = $"{TitlePrefixes[random.Next(TitlePrefixes.Length)]} {TitleSuffixes[random.Next(TitleSuffixes.Length)]}";

            catalog.Add(new ProviderWallpaper(
                ProviderName,
                $"mock-{i + 1:D3}",
                $"{title} {i + 1:D2}",
                $"Deterministic mock entry #{i + 1} for development and testing.",
                $"https://example.invalid/mock/{i + 1}",
                $"https://cdn.example.invalid/mock/{i + 1}.{(isVideo ? "mp4" : format.ToString().ToLowerInvariant())}",
                kind,
                format,
                width,
                height,
                isVideo ? 40_000_000L + random.Next(80_000_000) : 2_000_000L + random.Next(18_000_000),
                Licenses[random.Next(Licenses.Length)],
                "Mock Artist (dev data)",
                [.. categories.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)],
                dominant,
                Math.Round(random.NextDouble(), 2)));
        }

        return catalog;
    }
}
