namespace Deskverse.Providers;

using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>The outcome of one aggregated provider sweep.</summary>
public sealed record AggregatedResult<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<ProviderFailure> Failures)
{
    public bool AllFailed => Items.Count == 0 && Failures.Count > 0;

    public bool HasPartialFailure => Failures.Count > 0 && Items.Count > 0;
}

public sealed record ProviderFailure(string ProviderId, string DisplayName, string Error, bool TimedOut);

/// <summary>
/// Queries every registered provider concurrently and merges what succeeded.
/// A failing remote provider degrades discovery; it can never take the app down,
/// and local library results always flow through.
/// </summary>
public sealed class ProviderAggregator
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(8);

    private readonly IReadOnlyList<IWallpaperProvider> _providers;
    private readonly ILogger<ProviderAggregator> _logger;

    public ProviderAggregator(
        IEnumerable<IWallpaperProvider> providers,
        ILogger<ProviderAggregator> logger)
    {
        _providers = providers.OrderBy(p => p.ProviderId == LocalWallpaperProvider.ProviderName ? 0 : 1)
            .ThenBy(p => p.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _logger = logger;
    }

    public IReadOnlyList<IWallpaperProvider> Providers => _providers;

    public IWallpaperProvider? TryGet(string providerId) =>
        _providers.FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

    public async Task<AggregatedResult<ProviderWallpaper>> SearchAllAsync(
        WallpaperQuery query,
        CancellationToken cancellationToken = default)
    {
        var tasks = _providers.Select(p => SearchOneAsync(p, query, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return Merge(results);
    }

    public async Task<AggregatedResult<ProviderWallpaper>> TrendingAllAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var tasks = _providers.Select(p => TrendingOneAsync(p, count, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return Merge(results);
    }

    public async Task<AggregatedResult<string>> CategoriesAllAsync(
        CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(
            _providers.Select(async p =>
                (Provider: p, Result: await Wrap(p, p.GetCategoriesAsync(cancellationToken)))));
        var items = new List<string>();
        var failures = new List<ProviderFailure>();

        foreach (var (provider, result) in results)
        {
            if (result.Success && result.Items is { } categories)
            {
                foreach (var category in categories)
                {
                    if (!items.Contains(category, StringComparer.OrdinalIgnoreCase))
                    {
                        items.Add(category);
                    }
                }
            }
            else if (!result.Success)
            {
                failures.Add(new ProviderFailure(provider.ProviderId, provider.DisplayName, result.Error!, result.TimedOut));
            }
        }

        return new AggregatedResult<string>(
            items.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList(),
            failures);
    }

    private AggregatedResult<ProviderWallpaper> Merge(
        IReadOnlyList<(IWallpaperProvider Provider, ProviderResult<IReadOnlyList<ProviderWallpaper>> Result)> results)
    {
        var items = new List<ProviderWallpaper>();
        var failures = new List<ProviderFailure>();

        foreach (var (provider, result) in results)
        {
            if (result.Success && result.Items is { } wallpapers)
            {
                items.AddRange(wallpapers);
            }
            else if (!result.Success)
            {
                failures.Add(new ProviderFailure(provider.ProviderId, provider.DisplayName, result.Error!, result.TimedOut));
                _logger.LogWarning(
                    "Provider {Provider} failed: {Error} (timed out: {TimedOut})",
                    provider.ProviderId, result.Error, result.TimedOut);
            }
        }

        // Local library entries first, then remote providers, newest titles within each.
        return new AggregatedResult<ProviderWallpaper>(
            items
                .OrderBy(w => w.ProviderId == LocalWallpaperProvider.ProviderName ? 0 : 1)
                .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            failures);
    }

    private async Task<(IWallpaperProvider, ProviderResult<IReadOnlyList<ProviderWallpaper>>)> SearchOneAsync(
        IWallpaperProvider provider,
        WallpaperQuery query,
        CancellationToken cancellationToken)
    {
        var result = await Wrap(provider, provider.SearchAsync(query, cancellationToken));
        return (provider, result);
    }

    private async Task<(IWallpaperProvider, ProviderResult<IReadOnlyList<ProviderWallpaper>>)> TrendingOneAsync(
        IWallpaperProvider provider,
        int count,
        CancellationToken cancellationToken)
    {
        var result = await Wrap(provider, provider.GetTrendingAsync(count, cancellationToken));
        return (provider, result);
    }

    private async Task<ProviderResult<T>> Wrap<T>(IWallpaperProvider provider, Task<ProviderResult<T>> call)
    {
        try
        {
            return await call.WaitAsync(ProviderTimeout);
        }
        catch (TimeoutException)
        {
            return ProviderResult<T>.Fail($"Provider {provider.ProviderId} timed out.", timedOut: true);
        }
        catch (OperationCanceledException)
        {
            throw; // caller-initiated cancellation must propagate
        }
        catch (Exception ex)
        {
            return ProviderResult<T>.Fail($"Provider {provider.ProviderId} crashed: {ex.Message}");
        }
    }
}
