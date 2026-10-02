namespace Deskverse.IntegrationTests;

using Deskverse.Core.Entities;
using Deskverse.Intelligence;
using Deskverse.Storage;
using Xunit;

/// <summary>
/// Storage limit and LRU cleanup tests. These run entirely in-memory / with
/// temp directories and require no external services.
/// </summary>
public sealed class StorageLimitTests : IDisposable
{
    private readonly string _tempRoot;

    public StorageLimitTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"dv-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort
        }
    }

    [Fact]
    public void DiskAccounting_MeasuresExistingFiles()
    {
        var file = Path.Combine(_tempRoot, "test.bin");
        File.WriteAllBytes(file, new byte[1024]);

        var size = DiskAccounting.MeasureDirectory(_tempRoot);
        Assert.True(size >= 1024, $"Expected >= 1024, got {size}");
    }

    [Fact]
    public void DiskAccounting_EmptyDirectory_ReturnsZero()
    {
        var empty = Path.Combine(_tempRoot, "empty");
        Directory.CreateDirectory(empty);
        Assert.Equal(0, DiskAccounting.MeasureDirectory(empty));
    }

    [Fact]
    public void DiskAccounting_MissingDirectory_ReturnsZero()
    {
        Assert.Equal(0, DiskAccounting.MeasureDirectory(Path.Combine(_tempRoot, "nonexistent")));
    }

    [Fact]
    public void DiskAccounting_GetAvailableDiskBytes_NonNullForExistingPath()
    {
        var available = DiskAccounting.GetAvailableDiskBytes(_tempRoot);
        Assert.NotNull(available);
        Assert.True(available > 0);
    }

    [Fact]
    public void DuplicateDetector_LikelyDuplicates_SameHashGrouped()
    {
        // Reusing DuplicateDetector here as a lightweight integration check.
        var a = new Wallpaper
        {
            Id = Guid.NewGuid(),
            Title = "A",
            FileHash = "HASH1",
            Width = 1920,
            Height = 1080,
            DominantColor = "336699",
            FileSizeBytes = 1_000_000,
        };
        var b = new Wallpaper
        {
            Id = Guid.NewGuid(),
            Title = "B",
            FileHash = "HASH1",
            Width = 1920,
            Height = 1080,
            DominantColor = "336699",
            FileSizeBytes = 1_000_000,
        };

        var exactGroups = DuplicateDetector.FindExactDuplicates([a, b]);
        Assert.Single(exactGroups);
        Assert.True(exactGroups[0].Certain);
    }
}
