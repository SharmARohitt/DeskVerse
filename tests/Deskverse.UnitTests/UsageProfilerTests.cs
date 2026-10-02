namespace Deskverse.UnitTests;

using Deskverse.Core;
using Deskverse.Core.Entities;
using Deskverse.Intelligence;
using Xunit;

public sealed class UsageProfilerTests
{
    [Fact]
    public void BuildProfile_EmptyHistory_ReturnsEmptyProfile()
    {
        var profile = UsageProfiler.BuildProfile([], DateTimeOffset.UtcNow);
        Assert.Equal(0, profile.RecentUseTotal);
        Assert.Empty(profile.CategoryWeights);
    }

    [Fact]
    public void BuildProfile_SingleCategory_GetsWeightOne()
    {
        var wallpaper = new Wallpaper
        {
            Id = Guid.NewGuid(),
            Categories = ["Nature"],
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        var profile = UsageProfiler.BuildProfile([wallpaper], DateTimeOffset.UtcNow);

        Assert.Equal(1, profile.RecentUseTotal);
        Assert.True(profile.CategoryWeights.ContainsKey("Nature"));
        Assert.Equal(1.0, profile.CategoryWeights["Nature"], precision: 2);
    }

    [Fact]
    public void BuildProfile_MultipleCategories_MostUsedHasHighestWeight()
    {
        var wallpapers = Enumerable.Range(0, 5)
            .Select(_ => new Wallpaper
            {
                Id = Guid.NewGuid(),
                Categories = ["Cyberpunk"],
                LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1),
            })
            .Concat(new[]
            {
                new Wallpaper
                {
                    Id = Guid.NewGuid(),
                    Categories = ["Nature"],
                    LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1),
                },
            })
            .ToList();

        var profile = UsageProfiler.BuildProfile(wallpapers, DateTimeOffset.UtcNow);

        Assert.True(profile.CategoryWeights["Cyberpunk"] > profile.CategoryWeights["Nature"]);
        Assert.Equal(1.0, profile.CategoryWeights["Cyberpunk"], precision: 2);
    }

    [Fact]
    public void BuildProfile_OldUsage_IsExcluded()
    {
        var old = new Wallpaper
        {
            Id = Guid.NewGuid(),
            Categories = ["Space"],
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-90), // outside 60-day window
        };

        var profile = UsageProfiler.BuildProfile([old], DateTimeOffset.UtcNow, windowDays: 60);

        Assert.Equal(0, profile.RecentUseTotal);
        Assert.Empty(profile.CategoryWeights);
    }
}
