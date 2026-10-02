namespace Deskverse.UnitTests;

using Deskverse.Core;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Deskverse.Intelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class RecommendationEngineTests
{
    private static RecommendationEngine CreateEngine(RecommendationOptions? opts = null)
    {
        opts ??= new RecommendationOptions();
        return new RecommendationEngine(Options.Create(opts), NullLogger<RecommendationEngine>.Instance);
    }

    private static Wallpaper MakeWallpaper(
        string title = "Test",
        WallpaperKind kind = WallpaperKind.Static,
        string[] categories = null!,
        double? brightness = 0.4,
        string? dominantColor = "336699",
        int width = 1920,
        int height = 1080,
        int useCount = 0,
        DateTimeOffset? lastUsed = null)
    {
        return new Wallpaper
        {
            Id = Guid.NewGuid(),
            Title = title,
            Kind = kind,
            Categories = categories ?? ["Nature"],
            Brightness = brightness,
            DominantColor = dominantColor,
            Width = width,
            Height = height,
            UseCount = useCount,
            LastUsedAt = lastUsed,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        };
    }

    private static UserPreferences DefaultPreferences() => new()
    {
        PreferredCategories = ["Nature"],
        PreferredBrightness = 0.4,
    };

    private static DisplayInfo PrimaryDisplay() => new("PRIMARY", 0, 0, 1920, 1080, true, 1.0);

    private static UsageProfile EmptyProfile() => new(
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
        0);

    [Fact]
    public void Score_DislikedWallpaper_ScoreIsPenalized()
    {
        var engine = CreateEngine();
        var wallpaper = MakeWallpaper();
        var prefs = DefaultPreferences();
        var profile = EmptyProfile();
        var ctx = new CandidateContext(wallpaper, 0, null, IsDisliked: true);

        var result = engine.Score(wallpaper, prefs, PrimaryDisplay(), ctx, profile, DateTimeOffset.UtcNow,
            BackgroundResourcePolicy.Balanced);

        // Disliked penalty is -0.5 applied after normalization
        Assert.True(result.Score < 0.5, $"Expected score < 0.5 for disliked, got {result.Score}");
    }

    [Fact]
    public void Score_NewWallpaper_HasHighFreshness()
    {
        var engine = CreateEngine();
        var wallpaper = MakeWallpaper();
        wallpaper.CreatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        wallpaper.UseCount = 0;

        var prefs = DefaultPreferences();
        var profile = EmptyProfile();
        var ctx = new CandidateContext(wallpaper, 0, null, false);

        var result = engine.Score(wallpaper, prefs, PrimaryDisplay(), ctx, profile, DateTimeOffset.UtcNow,
            BackgroundResourcePolicy.Balanced);

        var freshnessFactor = result.Factors.First(f => f.Name == "Freshness");
        Assert.Equal(1.0, freshnessFactor.Contribution, precision: 2);
    }

    [Fact]
    public void Score_RecentlyUsedHeavily_HasRepetitionPenalty()
    {
        var engine = CreateEngine();
        var wallpaper = MakeWallpaper(useCount: 15, lastUsed: DateTimeOffset.UtcNow.AddHours(-2));
        var prefs = DefaultPreferences();
        var profile = EmptyProfile();
        var ctx = new CandidateContext(wallpaper, 15, DateTimeOffset.UtcNow.AddHours(-2), false);

        var result = engine.Score(wallpaper, prefs, PrimaryDisplay(), ctx, profile, DateTimeOffset.UtcNow,
            BackgroundResourcePolicy.Balanced);

        var penalty = result.Factors.First(f => f.Name == "RepetitionPenalty");
        Assert.True(penalty.Contribution > 0.3, $"Expected high repetition penalty, got {penalty.Contribution}");
    }

    [Fact]
    public void Score_VideoOnConservativePolicy_HasResourcePenalty()
    {
        var engine = CreateEngine();
        var wallpaper = MakeWallpaper(kind: WallpaperKind.Video);
        var prefs = DefaultPreferences();
        var profile = EmptyProfile();
        var ctx = new CandidateContext(wallpaper, 0, null, false);

        var result = engine.Score(wallpaper, prefs, PrimaryDisplay(), ctx, profile, DateTimeOffset.UtcNow,
            BackgroundResourcePolicy.Conservative);

        var penalty = result.Factors.First(f => f.Name == "ResourceCostPenalty");
        Assert.True(penalty.Contribution > 0, "Video should incur resource cost penalty");
    }

    [Fact]
    public void Score_ExplanationIsNotEmpty()
    {
        var engine = CreateEngine();
        var wallpaper = MakeWallpaper();
        var prefs = DefaultPreferences();
        var ctx = new CandidateContext(wallpaper, 0, null, false);

        var result = engine.Score(wallpaper, prefs, PrimaryDisplay(), ctx, EmptyProfile(), DateTimeOffset.UtcNow,
            BackgroundResourcePolicy.Balanced);

        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
    }

    [Fact]
    public void Score_PreferenceMatchCategories_IncreasesScore()
    {
        var engine = CreateEngine();

        var matching = MakeWallpaper(categories: ["Nature"]);
        var nonMatching = MakeWallpaper(categories: ["Cars"]);
        var prefs = new UserPreferences { PreferredCategories = ["Nature"] };
        var profile = EmptyProfile();
        var now = DateTimeOffset.UtcNow;

        var scoreMatch = engine.Score(matching, prefs, PrimaryDisplay(),
            new CandidateContext(matching, 0, null, false), profile, now, BackgroundResourcePolicy.Balanced).Score;
        var scoreNoMatch = engine.Score(nonMatching, prefs, PrimaryDisplay(),
            new CandidateContext(nonMatching, 0, null, false), profile, now, BackgroundResourcePolicy.Balanced).Score;

        Assert.True(scoreMatch > scoreNoMatch,
            $"Category-matched wallpaper should score higher. Match={scoreMatch}, NoMatch={scoreNoMatch}");
    }

    [Fact]
    public void Rank_ExplorationShufflesBottomFraction()
    {
        var engine = CreateEngine(new RecommendationOptions { ExplorationStartFraction = 0.5 });
        var wallpapers = Enumerable.Range(0, 20)
            .Select(i => MakeWallpaper($"Wallpaper {i}"))
            .ToList();

        var prefs = DefaultPreferences();
        var profile = EmptyProfile();
        var now = DateTimeOffset.UtcNow;

        var candidates = wallpapers.Select(w =>
            (w, new CandidateContext(w, 0, null, false))).ToList();

        var ranked = engine.Rank(candidates, prefs, PrimaryDisplay(), profile, now, BackgroundResourcePolicy.Balanced);

        // All candidates should still be present.
        Assert.Equal(20, ranked.Count);
    }

    [Fact]
    public void Score_ScoreIsAlwaysBetweenZeroAndOne()
    {
        var engine = CreateEngine();
        var prefs = DefaultPreferences();
        var profile = EmptyProfile();
        var now = DateTimeOffset.UtcNow;

        // Test various edge cases
        var testCases = new[]
        {
            MakeWallpaper(brightness: 0.0, useCount: 100, lastUsed: DateTimeOffset.UtcNow),
            MakeWallpaper(brightness: 1.0, kind: WallpaperKind.Video),
            MakeWallpaper(brightness: null, dominantColor: null),
        };

        foreach (var w in testCases)
        {
            var ctx = new CandidateContext(w, w.UseCount, w.LastUsedAt, false);
            var result = engine.Score(w, prefs, PrimaryDisplay(), ctx, profile, now, BackgroundResourcePolicy.Conservative);
            Assert.InRange(result.Score, 0.0, 1.0);
        }
    }
}
