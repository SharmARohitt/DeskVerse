namespace Deskverse.Intelligence;

using Deskverse.Core;
using Deskverse.Core.Entities;

/// <summary>Builds learned usage profiles (category affinity) from applied-wallpaper history.</summary>
public static class UsageProfiler
{
    /// <summary>
    /// Weights each category by how often recently applied wallpapers carried it.
    /// The most-used category gets weight 1; others scale relative to it.
    /// </summary>
    public static UsageProfile BuildProfile(
        IReadOnlyList<Wallpaper> recentlyUsed,
        DateTimeOffset nowUtc,
        double windowDays = 60)
    {
        var counts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var totalRecent = 0;

        foreach (var wallpaper in recentlyUsed)
        {
            if (wallpaper.LastUsedAt is not { } lastUsed)
            {
                continue;
            }

            var ageDays = (nowUtc - lastUsed).TotalDays;
            if (ageDays > windowDays)
            {
                continue;
            }

            totalRecent++;
            var recency = 1.0 / (1.0 + ageDays / 14.0);
            foreach (var category in wallpaper.Categories)
            {
                counts[category] = counts.GetValueOrDefault(category) + recency;
            }
        }

        var max = counts.Values.DefaultIfEmpty(0).Max();
        var weights = max > 0
            ? counts.ToDictionary(kv => kv.Key, kv => kv.Value / max, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        return new UsageProfile(weights, totalRecent);
    }
}
