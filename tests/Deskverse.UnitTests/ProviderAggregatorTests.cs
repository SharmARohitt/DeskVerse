namespace Deskverse.UnitTests;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Deskverse.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Provider isolation: one slow, crashing, or offline provider must degrade
/// discovery into a partial result, never an application failure.
/// </summary>
public sealed class ProviderAggregatorTests
{
    [Fact]
    public async Task SearchAllAsync_MergesItemsFromEveryProvider()
    {
        var aggregator = new ProviderAggregator(
            [Fake("alpha", Item("alpha", "one")), Fake("beta", Item("beta", "two"))],
            NullLogger<ProviderAggregator>.Instance);

        var result = await aggregator.SearchAllAsync(new WallpaperQuery());

        Assert.Equal(2, result.Items.Count);
        Assert.Empty(result.Failures);
        Assert.False(result.AllFailed);
    }

    [Fact]
    public async Task SearchAllAsync_KeepsLocalLibraryResultsFirst()
    {
        var aggregator = new ProviderAggregator(
            [
                Fake("zeta-remote", Item("zeta-remote", "remote")),
                Fake("z-local", Item(LocalWallpaperProvider.ProviderName, "local"), local: true),
            ],
            NullLogger<ProviderAggregator>.Instance);

        var result = await aggregator.SearchAllAsync(new WallpaperQuery());

        Assert.Equal("local", result.Items[0].Title);
    }

    [Fact]
    public async Task SearchAllAsync_OneFailingProviderBecomesAPartialResult()
    {
        var aggregator = new ProviderAggregator(
            [Fake("good", Item("good", "kept")), Fake("bad", null, error: "provider exploded")],
            NullLogger<ProviderAggregator>.Instance);

        var result = await aggregator.SearchAllAsync(new WallpaperQuery());

        Assert.Single(result.Items);
        Assert.Single(result.Failures);
        Assert.True(result.HasPartialFailure);
        Assert.False(result.AllFailed);
        Assert.Equal("provider exploded", result.Failures[0].Error);
    }

    [Fact]
    public async Task SearchAllAsync_WhenEveryProviderFailsItIsReportedAsOffline()
    {
        var aggregator = new ProviderAggregator(
            [Fake("a", null, error: "no network"), Fake("b", null, error: "no network")],
            NullLogger<ProviderAggregator>.Instance);

        var result = await aggregator.SearchAllAsync(new WallpaperQuery());

        Assert.Empty(result.Items);
        Assert.Equal(2, result.Failures.Count);
        Assert.True(result.AllFailed);
    }

    [Fact]
    public async Task SearchAllAsync_ConvertsAThrownExceptionIntoAFailure()
    {
        var aggregator = new ProviderAggregator(
            [Fake("good", Item("good", "kept")), Throwing()],
            NullLogger<ProviderAggregator>.Instance);

        var result = await aggregator.SearchAllAsync(new WallpaperQuery());

        Assert.Single(result.Items);
        Assert.Contains(result.Failures, f => f.ProviderId == "thrower" && f.Error!.Contains("crashed"));
    }

    [Fact]
    public async Task SearchAllAsync_CutsOffASlowProviderAndMarksItTimedOut()
    {
        var slow = Fake("slow", null, error: "unreachable", delay: TimeSpan.FromSeconds(9));
        var aggregator = new ProviderAggregator(
            [slow, Fake("quick", Item("quick", "kept"))],
            NullLogger<ProviderAggregator>.Instance);

        var result = await aggregator.SearchAllAsync(new WallpaperQuery());

        Assert.Contains(result.Items, i => i.Title == "kept");
        var failure = Assert.Single(result.Failures);
        Assert.Equal("slow", failure.ProviderId);
        Assert.True(failure.TimedOut);
    }

    [Fact]
    public async Task SearchAllAsync_PropagatesCallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        var aggregator = new ProviderAggregator(
            [Fake("slow", null, error: "unreachable", delay: TimeSpan.FromSeconds(5))],
            NullLogger<ProviderAggregator>.Instance);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => aggregator.SearchAllAsync(new WallpaperQuery(), cts.Token));
    }

    [Fact]
    public async Task CategoriesAllAsync_DeduplicatesIgnoringCase()
    {
        var aggregator = new ProviderAggregator(
            [
                Fake("a", null, categories: ["Nature", "Anime"]),
                Fake("b", null, categories: ["nature", "Space"]),
            ],
            NullLogger<ProviderAggregator>.Instance);

        var result = await aggregator.CategoriesAllAsync();

        Assert.Equal(3, result.Items.Count);
        Assert.Contains("Nature", result.Items, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGet_IsCaseInsensitiveAndUnknownIdsYieldNull()
    {
        var aggregator = new ProviderAggregator(
            [Fake("wallhaven", null)],
            NullLogger<ProviderAggregator>.Instance);

        Assert.NotNull(aggregator.TryGet("WallHaven"));
        Assert.Null(aggregator.TryGet("nonexistent"));
    }

    private static ProviderWallpaper Item(string providerId, string title) =>
        new(providerId, $"id-{title}", title, null, null, null,
            WallpaperKind.Static, WallpaperFormat.Png, 1920, 1080, 1024,
            null, null, [], null, null);

    private static FakeProvider Fake(
        string providerId,
        ProviderWallpaper? item,
        string? error = null,
        bool local = false,
        TimeSpan? delay = null,
        string[]? categories = null) =>
        new(providerId, item is null ? [] : [item], error, local, delay, categories ?? []);

    private static ThrowingProvider Throwing() => new();

    private sealed class FakeProvider(
        string providerId,
        IReadOnlyList<ProviderWallpaper> items,
        string? error,
        bool local,
        TimeSpan? delay,
        string[] categories) : IWallpaperProvider
    {
        public string ProviderId { get; } = local ? LocalWallpaperProvider.ProviderName : providerId;

        public string DisplayName => ProviderId;

        public bool RequiresNetwork => !local;

        public async Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> SearchAsync(
            WallpaperQuery query,
            CancellationToken cancellationToken = default)
        {
            if (delay is { } wait)
            {
                await Task.Delay(wait, cancellationToken);
            }

            return error is null
                ? ProviderResult<IReadOnlyList<ProviderWallpaper>>.Ok(items)
                : ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail(error);
        }

        public Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> GetTrendingAsync(
            int count,
            CancellationToken cancellationToken = default) => SearchAsync(new WallpaperQuery(), cancellationToken);

        public Task<ProviderResult<ProviderWallpaper>> GetDetailsAsync(
            string sourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(items.Count == 0
                ? ProviderResult<ProviderWallpaper>.Fail("no such item")
                : ProviderResult<ProviderWallpaper>.Ok(items[0]));

        public Task<ProviderResult<IReadOnlyList<string>>> GetCategoriesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<string>>.Ok(categories));

        public Task<ProviderResult<ProviderAttribution>> GetAttributionAsync(
            string sourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<ProviderAttribution>.Fail("no attribution"));
    }

    private sealed class ThrowingProvider : IWallpaperProvider
    {
        public string ProviderId => "thrower";
        public string DisplayName => "Thrower";
        public bool RequiresNetwork => true;

        public Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> SearchAsync(
            WallpaperQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProviderResult<IReadOnlyList<ProviderWallpaper>>>(new InvalidOperationException("boom"));

        public Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> GetTrendingAsync(
            int count,
            CancellationToken cancellationToken = default) => SearchAsync(new WallpaperQuery(), cancellationToken);

        public Task<ProviderResult<ProviderWallpaper>> GetDetailsAsync(
            string sourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<ProviderWallpaper>.Fail("unavailable"));

        public Task<ProviderResult<IReadOnlyList<string>>> GetCategoriesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<string>>.Ok([]));

        public Task<ProviderResult<ProviderAttribution>> GetAttributionAsync(
            string sourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<ProviderAttribution>.Fail("unavailable"));
    }
}
