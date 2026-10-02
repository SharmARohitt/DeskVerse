namespace Deskverse.Providers;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;

/// <summary>
/// The always-available provider backed by the user's own library. It performs no
/// network access and mirrors the repository's query semantics, so the app keeps
/// working when every remote provider is unreachable.
/// </summary>
public sealed class LocalWallpaperProvider : IWallpaperProvider
{
    public const string ProviderName = "local";

    private readonly IWallpaperRepository _repository;

    public LocalWallpaperProvider(IWallpaperRepository repository)
    {
        _repository = repository;
    }

    public string ProviderId => ProviderName;

    public string DisplayName => "My Library";

    public bool RequiresNetwork => false;

    public async Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> SearchAsync(
        WallpaperQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await _repository.QueryAsync(query, cancellationToken);
            return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Ok(
                page.Items.Select(ToProviderWallpaper).ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail(
                $"Library query failed: {ex.Message}");
        }
    }

    public async Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> GetTrendingAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        // "Trending" locally means the most-used wallpapers, which is the closest
        // honest signal we have without a network.
        try
        {
            var query = new WallpaperQuery
            {
                SortBy = SortOrder.MostUsedFirst,
                Take = Math.Clamp(count, 1, 200),
            };
            var page = await _repository.QueryAsync(query, cancellationToken);
            return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Ok(
                page.Items.Select(ToProviderWallpaper).ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail(
                $"Library trending query failed: {ex.Message}");
        }
    }

    public async Task<ProviderResult<ProviderWallpaper>> GetDetailsAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(sourceId, out var id))
        {
            return ProviderResult<ProviderWallpaper>.Fail(
                "Local provider source ids are GUIDs.");
        }

        var wallpaper = await _repository.GetByIdAsync(id, cancellationToken);
        return wallpaper is null
            ? ProviderResult<ProviderWallpaper>.Fail($"No local wallpaper with id {sourceId}.")
            : ProviderResult<ProviderWallpaper>.Ok(ToProviderWallpaper(wallpaper));
    }

    public async Task<ProviderResult<IReadOnlyList<string>>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new WallpaperQuery { Take = 500 };
            var page = await _repository.QueryAsync(query, cancellationToken);
            var categories = page.Items
                .SelectMany(w => w.Categories)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return ProviderResult<IReadOnlyList<string>>.Ok(categories);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult<IReadOnlyList<string>>.Fail(
                $"Category listing failed: {ex.Message}");
        }
    }

    public async Task<ProviderResult<ProviderAttribution>> GetAttributionAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(sourceId, out var id))
        {
            return ProviderResult<ProviderAttribution>.Fail(
                "Local provider source ids are GUIDs.");
        }

        var wallpaper = await _repository.GetByIdAsync(id, cancellationToken);
        if (wallpaper is null)
        {
            return ProviderResult<ProviderAttribution>.Fail($"No local wallpaper with id {sourceId}.");
        }

        return ProviderResult<ProviderAttribution>.Ok(new ProviderAttribution(
            ProviderId,
            wallpaper.Id.ToString("D"),
            wallpaper.License,
            wallpaper.Attribution,
            wallpaper.IsUserImported ? wallpaper.OriginalImportPath : wallpaper.SourceUrl,
            wallpaper.IsUserImported
                ? "Imported from your own files. DeskVerse never redistributes local imports."
                : "Served from the local library."));
    }

    internal static ProviderWallpaper ToProviderWallpaper(Wallpaper w) => new(
        ProviderName,
        w.Id.ToString("D"),
        w.Title,
        w.Description,
        w.IsUserImported ? w.OriginalImportPath : w.SourceUrl,
        null, // Local items are already on disk; there is no download step.
        w.Kind,
        w.Format,
        w.Width,
        w.Height,
        w.FileSizeBytes,
        w.License,
        w.Attribution,
        w.Categories,
        w.DominantColor,
        w.Brightness);
}
