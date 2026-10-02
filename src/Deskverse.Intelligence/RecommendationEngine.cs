namespace Deskverse.Intelligence;

using Deskverse.Core;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Everything the scorer needs to know about one candidate.</summary>
public sealed record CandidateContext(
    Wallpaper Wallpaper,
    int UseCount,
    DateTimeOffset? LastUsedAt,
    bool IsDisliked);

/// <summary>Affinity learned from usage history: category weights and recency.</summary>
public sealed record UsageProfile(
    IReadOnlyDictionary<string, double> CategoryWeights,
    int RecentUseTotal);

/// <summary>
/// Explainable weighted recommendation engine. Every score is decomposed into
/// named factors so the UI can tell users exactly why something was suggested.
/// </summary>
public sealed class RecommendationEngine
{
    private readonly RecommendationOptions _options;
    private readonly ILogger<RecommendationEngine> _logger;

    public RecommendationEngine(IOptions<RecommendationOptions> options, ILogger<RecommendationEngine> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Scores one wallpaper and returns the full factor decomposition plus a human explanation.</summary>
    public Recommendation Score(
        Wallpaper wallpaper,
        UserPreferences preferences,
        DisplayInfo primaryDisplay,
        CandidateContext usage,
        UsageProfile profile,
        DateTimeOffset nowUtc,
        BackgroundResourcePolicy resourcePolicy)
    {
        ArgumentNullException.ThrowIfNull(wallpaper);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(profile);

        var factors = new List<RecommendationFactor>
        {
            ScorePreferenceMatch(wallpaper, preferences),
            ScoreVisualCompatibility(wallpaper),
            ScoreResolutionMatch(wallpaper, primaryDisplay),
            ScoreCategoryAffinity(wallpaper, profile),
            ScoreFreshness(wallpaper, nowUtc),
            ScoreRepetitionPenalty(usage, nowUtc),
            ScoreResourceCost(wallpaper, resourcePolicy),
        };

        var positiveTotal = _options.PreferenceMatchWeight
            + _options.VisualCompatibilityWeight
            + _options.ResolutionMatchWeight
            + _options.CategoryAffinityWeight
            + _options.FreshnessWeight;

        var score = 0.0;
        foreach (var factor in factors)
        {
            score += factor.Weight >= 0
                ? factor.Weight / positiveTotal * factor.Contribution
                : factor.Weight * factor.Contribution;
        }

        if (usage.IsDisliked)
        {
            score -= 0.5;
        }

        score = Math.Clamp(score, 0, 1);
        var explanation = BuildExplanation(wallpaper, preferences, primaryDisplay, factors);
        return new Recommendation(wallpaper, score, factors, explanation);
    }

    /// <summary>Scores and ranks a batch of candidates, best first.</summary>
    public IReadOnlyList<Recommendation> Rank(
        IEnumerable<(Wallpaper Wallpaper, CandidateContext Usage)> candidates,
        UserPreferences preferences,
        DisplayInfo primaryDisplay,
        UsageProfile profile,
        DateTimeOffset nowUtc,
        BackgroundResourcePolicy resourcePolicy)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var recommendations = new List<Recommendation>();
        foreach (var (wallpaper, usage) in candidates)
        {
            try
            {
                recommendations.Add(Score(wallpaper, preferences, primaryDisplay, usage, profile, nowUtc, resourcePolicy));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scoring failed for {Wallpaper}; skipping", wallpaper.Id);
            }
        }

        // Sort descending by score.
        recommendations.Sort((a, b) => b.Score.CompareTo(a.Score));

        // 80/20 exploration: the bottom 20% of the list gets a random shuffle
        // so the same top-10 wallpapers don't dominate every recommendation call.
        var explorationStart = Math.Max(1, (int)(recommendations.Count * _options.ExplorationStartFraction));
        if (explorationStart < recommendations.Count)
        {
            var explorationSlice = recommendations.GetRange(explorationStart, recommendations.Count - explorationStart);
            Shuffle(explorationSlice);
            for (var i = explorationStart; i < recommendations.Count; i++)
            {
                recommendations[i] = explorationSlice[i - explorationStart];
            }
        }

        return recommendations;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private RecommendationFactor ScorePreferenceMatch(Wallpaper wallpaper, UserPreferences preferences)
    {
        var details = new List<string>();
        double categoryScore = 0;

        if (preferences.PreferredCategories.Length > 0 && wallpaper.Categories.Length > 0)
        {
            var preferred = preferences.PreferredCategories
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var overlap = wallpaper.Categories.Count(c => preferred.Contains(c));
            categoryScore = (double)overlap / preferences.PreferredCategories.Length;
            if (overlap > 0)
            {
                var matched = wallpaper.Categories.Where(c => preferred.Contains(c)).Take(2);
                details.Add($"categories: {string.Join(", ", matched)}");
            }
        }

        double colorScore = 0;
        if (preferences.PreferredColors.Length > 0 && wallpaper.DominantColor is { } color)
        {
            colorScore = preferences.PreferredColors
                .Select(preferred => 1.0 - ColorMath.ColorDistance(preferred, color))
                .DefaultIfEmpty(0)
                .Max();
            if (colorScore > 0.75)
            {
                details.Add($"{ColorMath.DescribeColor(color)} tones");
            }
        }

        double brightnessScore = 0.5;
        if (preferences.PreferredBrightness is { } target && wallpaper.Brightness is { } actual)
        {
            brightnessScore = 1.0 - Math.Clamp(Math.Abs(actual - target) * 2.5, 0, 1);
            var tone = actual < 0.35 ? "dark aesthetic" : actual > 0.7 ? "bright look" : "balanced lighting";
            details.Add(tone);
        }

        var contribution = 0.5 * categoryScore + 0.3 * colorScore + 0.2 * brightnessScore;
        var detail = details.Count > 0
            ? string.Join("; ", details)
            : "no explicit preferences configured yet";
        return new RecommendationFactor("PreferenceMatch", _options.PreferenceMatchWeight, contribution, detail);
    }

    private RecommendationFactor ScoreVisualCompatibility(Wallpaper wallpaper)
    {
        // Desktop backgrounds read best at mid-low brightness and low visual noise.
        double brightness = 0.5;
        if (wallpaper.Brightness is { } b)
        {
            brightness = 1.0 - Math.Clamp(Math.Abs(b - 0.35) * 1.8, 0, 1);
        }

        double density = 0.7;
        if (wallpaper.VisualDensity is { } d)
        {
            density = 1.0 - Math.Clamp(Math.Abs(d - 0.15) * 1.6, 0, 1);
        }

        var contribution = 0.6 * brightness + 0.4 * density;
        var detail = wallpaper.Brightness is { } bb && wallpaper.VisualDensity is { } dd
            ? $"brightness {bb:0.00}, visual noise {dd:0.00} — {(contribution > 0.6 ? "comfortable as a background" : "demanding as a background")}"
            : "not yet analyzed";
        return new RecommendationFactor("VisualCompatibility", _options.VisualCompatibilityWeight, contribution, detail);
    }

    private RecommendationFactor ScoreResolutionMatch(Wallpaper wallpaper, DisplayInfo display)
    {
        if (display.Width <= 0 || display.Height <= 0)
        {
            return new RecommendationFactor("ResolutionMatch", _options.ResolutionMatchWeight, 0.5, "display unknown");
        }

        if (wallpaper.Width == display.Width && wallpaper.Height == display.Height)
        {
            return new RecommendationFactor("ResolutionMatch", _options.ResolutionMatchWeight, 1.0,
                $"pixel-perfect for {display.Width}×{display.Height}");
        }

        var displayAspect = (double)display.Width / display.Height;
        var wallpaperAspect = wallpaper.AspectRatio;
        var aspectDelta = Math.Abs(displayAspect - wallpaperAspect);

        if (aspectDelta < 0.02 && wallpaper.Width >= display.Width)
        {
            return new RecommendationFactor("ResolutionMatch", _options.ResolutionMatchWeight, 0.85,
                $"same aspect ratio and larger than {display.Width}×{display.Height}");
        }

        if (aspectDelta < 0.02)
        {
            var scale = (double)wallpaper.Width / display.Width;
            return new RecommendationFactor("ResolutionMatch", _options.ResolutionMatchWeight, Math.Clamp(0.5 + scale / 2, 0.3, 0.7),
                $"same aspect ratio but will upscale {1 / Math.Max(scale, 0.01):0.0}× on {display.Width}×{display.Height}");
        }

        if (wallpaper.Width >= display.Width && wallpaper.Height >= display.Height)
        {
            return new RecommendationFactor("ResolutionMatch", _options.ResolutionMatchWeight, 0.45,
                $"different aspect ratio ({wallpaper.Width}×{wallpaper.Height}) than the {display.Width}×{display.Height} display");
        }

        return new RecommendationFactor("ResolutionMatch", _options.ResolutionMatchWeight, 0.2,
            $"smaller than the {display.Width}×{display.Height} display");
    }

    private RecommendationFactor ScoreCategoryAffinity(Wallpaper wallpaper, UsageProfile profile)
    {
        if (profile.CategoryWeights.Count == 0 || wallpaper.Categories.Length == 0)
        {
            return new RecommendationFactor("CategoryAffinity", _options.CategoryAffinityWeight, 0.4,
                "not enough usage history to learn affinity yet");
        }

        var best = wallpaper.Categories
            .Select(c => profile.CategoryWeights.TryGetValue(c, out var weight) ? weight : 0)
            .DefaultIfEmpty(0)
            .Max();
        var contribution = Math.Clamp(best, 0, 1);
        var detail = contribution > 0.6
            ? $"you often use {wallpaper.Categories.First(c => profile.CategoryWeights.TryGetValue(c, out var w) && w > 0.6)} wallpapers"
            : contribution > 0.2 ? "some overlap with what you use" : "outside your usual categories";
        return new RecommendationFactor("CategoryAffinity", _options.CategoryAffinityWeight, contribution, detail);
    }

    private RecommendationFactor ScoreFreshness(Wallpaper wallpaper, DateTimeOffset nowUtc)
    {
        if (wallpaper.UseCount == 0)
        {
            return new RecommendationFactor("Freshness", _options.FreshnessWeight, 1.0, "new to your library");
        }

        var ageDays = Math.Max(0, (nowUtc - wallpaper.CreatedAt).TotalDays);
        var contribution = 1.0 / (1.0 + ageDays / Math.Max(1, _options.FreshnessHalfLifeDays));
        return new RecommendationFactor("Freshness", _options.FreshnessWeight, contribution,
            $"added {ageDays:N0} days ago");
    }

    private RecommendationFactor ScoreRepetitionPenalty(CandidateContext usage, DateTimeOffset nowUtc)
    {
        if (usage.LastUsedAt is not { } lastUsed)
        {
            return new RecommendationFactor("RepetitionPenalty", _options.RepetitionPenaltyWeight, 0, "never used");
        }

        var daysSince = Math.Max(0, (nowUtc - lastUsed).TotalDays);
        if (daysSince > _options.RepetitionWindowDays)
        {
            return new RecommendationFactor("RepetitionPenalty", _options.RepetitionPenaltyWeight, 0,
                $"last used {daysSince:N0} days ago");
        }

        // Recency dominates; heavy recent use pushes toward the full penalty.
        var recency = 1.0 - Math.Clamp(daysSince / _options.RepetitionWindowDays, 0, 1);
        var intensity = Math.Clamp(usage.UseCount / 10.0, 0, 1);
        var contribution = Math.Clamp(0.5 * recency + 0.5 * intensity, 0, 1);
        return new RecommendationFactor("RepetitionPenalty", _options.RepetitionPenaltyWeight, contribution,
            $"used {usage.UseCount}× recently, last {daysSince:N0} day(s) ago");
    }

    private RecommendationFactor ScoreResourceCost(Wallpaper wallpaper, BackgroundResourcePolicy policy)
    {
        var cost = 0.0;
        var details = new List<string>();

        if (wallpaper.Kind == WallpaperKind.Video)
        {
            var videoCost = policy switch
            {
                BackgroundResourcePolicy.Conservative => _options.VideoResourceCost,
                BackgroundResourcePolicy.Balanced => _options.VideoResourceCost * 0.7,
                _ => _options.VideoResourceCost * 0.4,
            };
            cost += videoCost;
            details.Add("video playback overhead");
        }

        if (wallpaper.FileSizeBytes > _options.LargeFileThresholdBytes)
        {
            cost += _options.LargeFileResourceCost;
            details.Add($"{wallpaper.FileSizeBytes / (1024.0 * 1024):N0} MB file");
        }

        var contribution = Math.Clamp(cost, 0, 1);
        return new RecommendationFactor("ResourceCostPenalty", _options.ResourceCostPenaltyWeight, contribution,
            details.Count > 0 ? string.Join("; ", details) : "low playback overhead");
    }

    private static string BuildExplanation(
        Wallpaper wallpaper,
        UserPreferences preferences,
        DisplayInfo display,
        IReadOnlyList<RecommendationFactor> factors)
    {
        var positives = factors
            .Where(f => f.Weight >= 0)
            .OrderByDescending(f => f.Weight * f.Contribution)
            .Take(3)
            .Where(f => f.Contribution >= 0.5)
            .Select(f => f.Detail)
            .ToList();

        var penalties = factors
            .Where(f => f.Weight < 0 && f.Contribution > 0.3)
            .Select(f => f.Detail)
            .ToList();

        var segments = new List<string>();
        if (positives.Count > 0)
        {
            segments.Add($"matches {string.Join(", and ", positives)}");
        }

        if (penalties.Count > 0)
        {
            segments.Add($"watch out: {string.Join(", ", penalties)}");
        }

        return segments.Count > 0
            ? $"Recommended because it {string.Join("; ", segments)}."
            : "Recommended from your library with neutral scores so far.";
    }
}
