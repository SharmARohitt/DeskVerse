namespace Deskverse.UnitTests;

using Deskverse.Core.Entities;
using Deskverse.Intelligence;
using Xunit;

public sealed class DuplicateDetectorTests
{
    [Fact]
    public void FindExactDuplicates_NoDuplicates_ReturnsEmpty()
    {
        var wallpapers = new[]
        {
            new Wallpaper { Id = Guid.NewGuid(), FileHash = "AAAAAA" },
            new Wallpaper { Id = Guid.NewGuid(), FileHash = "BBBBBB" },
        };

        var groups = DuplicateDetector.FindExactDuplicates(wallpapers);
        Assert.Empty(groups);
    }

    [Fact]
    public void FindExactDuplicates_SameHash_GroupsThem()
    {
        var a = new Wallpaper { Id = Guid.NewGuid(), Title = "A", FileHash = "SAME" };
        var b = new Wallpaper { Id = Guid.NewGuid(), Title = "B", FileHash = "SAME" };
        var c = new Wallpaper { Id = Guid.NewGuid(), Title = "C", FileHash = "DIFFERENT" };

        var groups = DuplicateDetector.FindExactDuplicates([a, b, c]);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Wallpapers.Count);
        Assert.Contains(groups[0].Wallpapers, w => w.Id == a.Id);
        Assert.Contains(groups[0].Wallpapers, w => w.Id == b.Id);
    }

    [Fact]
    public void FindLikelyDuplicates_DifferentSizes_ReturnsEmpty()
    {
        var a = new Wallpaper { Id = Guid.NewGuid(), Width = 1920, Height = 1080 };
        var b = new Wallpaper { Id = Guid.NewGuid(), Width = 3840, Height = 2160 };

        var groups = DuplicateDetector.FindLikelyDuplicates([a, b]);
        Assert.Empty(groups);
    }
}
