namespace Deskverse.Intelligence;

/// <summary>
/// Configurable scoring weights for the recommendation engine. Positive weights
/// are normalized internally; penalty weights are negative and applied as-is.
/// </summary>
public sealed class RecommendationOptions
{
    /// <summary>Agreement with explicit user preferences (categories, colors, brightness).</summary>
    public double PreferenceMatchWeight { get; set; } = 0.30;

    /// <summary>Suitability as a desktop background (calm brightness, low visual noise).</summary>
    public double VisualCompatibilityWeight { get; set; } = 0.10;

    /// <summary>How well the resolution fits the primary display.</summary>
    public double ResolutionMatchWeight { get; set; } = 0.20;

    /// <summary>Affinity derived from actually applied wallpapers, not just declared taste.</summary>
    public double CategoryAffinityWeight { get; set; } = 0.15;

    /// <summary>Boost for content new to the library.</summary>
    public double FreshnessWeight { get; set; } = 0.10;

    /// <summary>Penalty for recently or heavily repeated wallpapers.</summary>
    public double RepetitionPenaltyWeight { get; set; } = -0.10;

    /// <summary>Penalty for expensive playback (video, very large files) scaled by resource policy.</summary>
    public double ResourceCostPenaltyWeight { get; set; } = -0.05;

    /// <summary>Days over which freshness decays to half.</summary>
    public double FreshnessHalfLifeDays { get; set; } = 30;

    /// <summary>Days considered "recent" for repetition.</summary>
    public double RepetitionWindowDays { get; set; } = 7;

    /// <summary>Video playback counts as expensive unless the policy is HighQuality.</summary>
    public double VideoResourceCost { get; set; } = 0.6;

    public double LargeFileThresholdBytes { get; set; } = 128L * 1024 * 1024;

    public double LargeFileResourceCost { get; set; } = 0.3;

    public static RecommendationOptions Default { get; } = new();
}
