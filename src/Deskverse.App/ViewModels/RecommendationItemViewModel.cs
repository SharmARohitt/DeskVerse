namespace Deskverse.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deskverse.Core.Models;

/// <summary>One explainable recommendation: the wallpaper plus its scoring story.</summary>
public partial class RecommendationItemViewModel : ObservableObject
{
    public RecommendationItemViewModel(Recommendation recommendation, WallpaperItemViewModel wallpaper)
    {
        Recommendation = recommendation;
        Wallpaper = wallpaper;
        Factors = recommendation.Factors
            .Select(f => new RecommendationFactorViewModel(f))
            .OrderByDescending(f => f.Contribution)
            .ToArray();
    }

    public Recommendation Recommendation { get; }

    public WallpaperItemViewModel Wallpaper { get; }

    public IReadOnlyList<RecommendationFactorViewModel> Factors { get; }

    public string ScoreText => $"{Recommendation.Score:N2}";

    public string Explanation => Recommendation.Explanation;

    public double ScorePercent => Math.Clamp(Recommendation.Score * 100, 0, 100);
}

public partial class RecommendationFactorViewModel : ObservableObject
{
    public RecommendationFactorViewModel(RecommendationFactor factor)
    {
        Name = factor.Name;
        WeightText = $"{factor.Weight:N2}";
        Detail = factor.Detail;
        ContributionPercent = Math.Clamp(Math.Abs(factor.Contribution) * 100, 0, 100);
    }

    public string Name { get; }

    public string WeightText { get; }

    public string Detail { get; }

    public double ContributionPercent { get; }
}
