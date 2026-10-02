namespace Deskverse.Application;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Deskverse.Intelligence;
using Microsoft.Extensions.Logging;

/// <summary>
/// Explainable recommendations from the library plus learned usage affinity,
/// and the "Surprise me" flow that applies the best suggestion.
/// </summary>
public sealed class RecommendationService
{
    private const int CandidatePoolSize = 200;

    private readonly IWallpaperRepository _repository;
    private readonly IPreferencesStore _preferencesStore;
    private readonly IDisplayService _displayService;
    private readonly IClock _clock;
    private readonly RecommendationEngine _engine;
    private readonly WallpaperManager _wallpaperManager;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        IWallpaperRepository repository,
        IPreferencesStore preferencesStore,
        IDisplayService displayService,
        IClock clock,
        RecommendationEngine engine,
        WallpaperManager wallpaperManager,
        ILogger<RecommendationService> logger)
    {
        _repository = repository;
        _preferencesStore = preferencesStore;
        _displayService = displayService;
        _clock = clock;
        _engine = engine;
        _wallpaperManager = wallpaperManager;
        _logger = logger;
    }

    /// <summary>Ranked recommendations with per-factor explanations, best first.</summary>
    public async Task<IReadOnlyList<Recommendation>> GetRecommendationsAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 50);
        var nowUtc = _clock.UtcNow;

        var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var display = _displayService.GetPrimaryDisplay();

        var recentlyUsed = await _repository
            .GetRecentlyUsedAsync(50, cancellationToken)
            .ConfigureAwait(false);
        var profile = UsageProfiler.BuildProfile(recentlyUsed, nowUtc);

        var pool = await _repository.QueryAsync(
            new WallpaperQuery { ExcludeDisliked = true, Take = CandidatePoolSize },
            cancellationToken).ConfigureAwait(false);

        if (pool.Items.Count == 0)
        {
            return [];
        }

        var candidates = pool.Items
            .Select(w => (w, new CandidateContext(w, w.UseCount, w.LastUsedAt, w.IsDisliked)))
            .ToList();

        var ranked = _engine.Rank(candidates, preferences, display, profile, nowUtc, preferences.BackgroundPolicy);
        _logger.LogInformation(
            "Ranked {Total} candidates into {Returned} recommendations (profile from {Recent} recent uses).",
            pool.Items.Count, Math.Min(count, ranked.Count), profile.RecentUseTotal);

        return ranked.Take(count).ToList();
    }

    /// <summary>Applies the top recommendation and returns it with its explanation.</summary>
    public async Task<(Recommendation? Recommendation, ApplyOutcome Outcome)> SurpriseMeAsync(
        CancellationToken cancellationToken = default)
    {
        var best = await GetRecommendationsAsync(1, cancellationToken).ConfigureAwait(false);
        if (best.Count == 0)
        {
            return (null, ApplyOutcome.Fail(
                "Nothing to recommend yet. Import wallpapers or download some from Discover first."));
        }

        var recommendation = best[0];
        var outcome = await _wallpaperManager
            .ApplyAsync(recommendation.Wallpaper.Id, null, cancellationToken)
            .ConfigureAwait(false);
        return (recommendation, outcome);
    }
}
