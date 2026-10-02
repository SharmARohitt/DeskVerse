namespace Deskverse.Intelligence;

using Deskverse.Core.Entities;

public sealed record DuplicateGroup(
    string FileHash,
    IReadOnlyList<Wallpaper> Wallpapers,
    bool Certain);

/// <summary>
/// Duplicate detection over the wallpaper library. Exact matches are certain
/// (same content hash); heuristics flag likely re-encodes for user review.
/// </summary>
public static class DuplicateDetector
{
    /// <summary>Groups wallpapers that share a content hash.</summary>
    public static IReadOnlyList<DuplicateGroup> FindExactDuplicates(IEnumerable<Wallpaper> wallpapers)
    {
        ArgumentNullException.ThrowIfNull(wallpapers);

        return wallpapers
            .Where(w => !string.IsNullOrEmpty(w.FileHash))
            .GroupBy(w => w.FileHash, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup(g.Key, g.ToList(), Certain: true))
            .ToList();
    }

    /// <summary>
    /// Heuristic near-duplicates: same pixel dimensions, same dominant color,
    /// and file sizes within 2%. Marked as uncertain, for user confirmation.
    /// </summary>
    public static IReadOnlyList<DuplicateGroup> FindLikelyDuplicates(IEnumerable<Wallpaper> wallpapers)
    {
        ArgumentNullException.ThrowIfNull(wallpapers);

        var candidates = wallpapers.Where(w => w.Width > 0 && w.DominantColor is not null).ToList();
        var groups = new List<DuplicateGroup>();
        var assigned = new HashSet<Guid>();

        candidates.Sort((a, b) => a.FileSizeBytes.CompareTo(b.FileSizeBytes));
        for (var i = 0; i < candidates.Count; i++)
        {
            if (!assigned.Add(candidates[i].Id))
            {
                continue;
            }

            var group = new List<Wallpaper> { candidates[i] };
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var other = candidates[j];
                if (assigned.Contains(other.Id))
                {
                    continue;
                }

                if (other.FileSizeBytes - candidates[i].FileSizeBytes > candidates[i].FileSizeBytes * 0.02)
                {
                    // Sorted by size: nothing further can be within 2%.
                    break;
                }

                if (other.Width == candidates[i].Width
                    && other.Height == candidates[i].Height
                    && string.Equals(other.DominantColor, candidates[i].DominantColor, StringComparison.OrdinalIgnoreCase))
                {
                    group.Add(other);
                    assigned.Add(other.Id);
                }
            }

            if (group.Count > 1)
            {
                groups.Add(new DuplicateGroup(string.Empty, group, Certain: false));
            }
        }

        return groups;
    }
}
