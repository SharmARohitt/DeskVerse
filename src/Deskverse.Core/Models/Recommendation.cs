namespace Deskverse.Core.Models;

using Deskverse.Core.Entities;

/// <summary>An explainable recommendation produced by the intelligence engine.</summary>
public sealed record Recommendation(
    Wallpaper Wallpaper,
    double Score,
    IReadOnlyList<RecommendationFactor> Factors,
    string Explanation);

/// <summary>One weighted factor contributing to a recommendation score.</summary>
public sealed record RecommendationFactor(
    string Name,
    double Weight,
    double Contribution,
    string Detail);
