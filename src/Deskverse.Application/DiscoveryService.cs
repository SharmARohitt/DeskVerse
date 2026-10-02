namespace Deskverse.Application;

using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Deskverse.Providers;
using Microsoft.Extensions.Logging;

/// <summary>
/// Read side of content discovery: provider search, trending, categories, and
/// attribution. Downloads are delegated to <see cref="WallpaperManager"/> so every
/// byte entering the cache goes through the same hardened pipeline.
/// </summary>
public sealed class DiscoveryService
{
    private readonly ProviderAggregator _providers;
    private readonly WallpaperManager _wallpaperManager;
    private readonly ILogger<DiscoveryService> _logger;

    public DiscoveryService(ProviderAggregator providers, WallpaperManager wallpaperManager, ILogger<DiscoveryService> logger)
    {
        _providers = providers;
        _wallpaperManager = wallpaperManager;
        _logger = logger;
    }

    /// <summary>Every registered provider, for the Discover page's source picker.</summary>
    public IReadOnlyList<ProviderInfo> GetProviders() =>
        _providers.Providers.Select(p => new ProviderInfo(p.ProviderId, p.DisplayName, p.RequiresNetwork)).ToList();

    public async Task<AggregatedResult<ProviderWallpaper>> SearchAsync(
        WallpaperQuery query,
        CancellationToken cancellationToken = default)
    {
        var result = await _providers.SearchAllAsync(query, cancellationToken).ConfigureAwait(false);
        LogFailures("search", result.Failures);
        return result;
    }

    public async Task<AggregatedResult<ProviderWallpaper>> GetTrendingAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var result = await _providers.TrendingAllAsync(count, cancellationToken).ConfigureAwait(false);
        LogFailures("trending", result.Failures);
        return result;
    }

    public async Task<AggregatedResult<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _providers.CategoriesAllAsync(cancellationToken).ConfigureAwait(false);
        LogFailures("categories", result.Failures);
        return result;
    }

    public async Task<ProviderResult<ProviderAttribution>> GetAttributionAsync(
        string providerId,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.TryGet(providerId);
        if (provider is null)
        {
            return ProviderResult<ProviderAttribution>.Fail($"Unknown provider '{providerId}'.");
        }

        return await provider.GetAttributionAsync(sourceId, cancellationToken).ConfigureAwait(false);
    }

    public Task<DownloadOutcome> DownloadAsync(
        string providerId,
        string sourceId,
        CancellationToken cancellationToken = default) =>
        _wallpaperManager.DownloadAsync(providerId, sourceId, cancellationToken);

    private void LogFailures(string operation, IReadOnlyList<ProviderFailure> failures)
    {
        foreach (var failure in failures)
        {
            _logger.LogWarning(
                "Provider {Provider} failed during {Operation}: {Error} (timed out: {TimedOut})",
                failure.ProviderId, operation, failure.Error, failure.TimedOut);
        }
    }
}

/// <summary>Descriptor for one registered wallpaper source.</summary>
public sealed record ProviderInfo(string ProviderId, string DisplayName, bool RequiresNetwork);
