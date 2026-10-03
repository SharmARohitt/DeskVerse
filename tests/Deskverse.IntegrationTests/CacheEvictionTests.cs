namespace Deskverse.IntegrationTests;

using Deskverse.Core;
using Deskverse.Core.Entities;
using Deskverse.Infrastructure.Persistence;
using Deskverse.Infrastructure.Repositories;
using Deskverse.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// LRU eviction against a real SQLite repository and a real cache directory on
/// disk. Cleanup must free space without ever touching pinned or active content.
/// </summary>
public sealed class CacheEvictionTests : IDisposable
{
    private const long Mb = 1024 * 1024;

    private readonly string _root;
    private readonly string _cacheRoot;
    private readonly ServiceProvider _provider;
    private readonly WallpaperRepository _repository;

    public CacheEvictionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dv-cache-{Guid.NewGuid():N}");
        _cacheRoot = Path.Combine(_root, "cache");
        Directory.CreateDirectory(_cacheRoot);

        _provider = new ServiceCollection()
            .AddDbContextFactory<DeskverseDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(_root, "test.db")}"))
            .BuildServiceProvider();

        var factory = _provider.GetRequiredService<IDbContextFactory<DeskverseDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        _repository = new WallpaperRepository(factory);
    }

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp sandbox.
        }
    }

    [Fact]
    public async Task EnsureSpaceAsync_EvictsOnlyTheLeastRecentlyUsedEntry()
    {
        var oldest = await SeedAsync("oldest", 2 * Mb, lastAccessTicks: 1_000);
        var newer = await SeedAsync("newer", 4 * Mb, lastAccessTicks: 2_000);
        var active = await SeedAsync("active", 4 * Mb, lastAccessTicks: 500);

        // 10 MB used plus 1 MB incoming is 1 MB over the limit, so one 2 MB
        // eviction is enough and the newer entry must be left alone.
        var cache = CreateCache(limitBytes: 10 * Mb);
        var result = await cache.EnsureSpaceAsync(1 * Mb, active.Id);

        Assert.True(result.EnoughSpace);
        Assert.False(File.Exists(Absolute(oldest)));
        Assert.True(File.Exists(Absolute(newer)));
        Assert.True(File.Exists(Absolute(active)), "The active wallpaper must never be evicted.");
        Assert.Equal(2 * Mb, result.FreedBytes);
        Assert.Equal(9 * Mb, result.ProjectedUsedBytes);
    }

    [Fact]
    public async Task EnsureSpaceAsync_NeverEvictsPinnedOrActiveWallpapers()
    {
        var stale = await SeedAsync("stale", 2 * Mb, lastAccessTicks: 1_000);
        var staler = await SeedAsync("staler", 4 * Mb, lastAccessTicks: 2_000);
        var pinned = await SeedAsync("pinned", 4 * Mb, lastAccessTicks: 3_000, pinned: true);
        var active = await SeedAsync("active", 4 * Mb, lastAccessTicks: 4_000);

        var cache = CreateCache(limitBytes: 12 * Mb);
        var result = await cache.EnsureSpaceAsync(3 * Mb, active.Id);

        Assert.True(result.EnoughSpace);
        Assert.False(File.Exists(Absolute(stale)));
        Assert.False(File.Exists(Absolute(staler)));
        Assert.True(File.Exists(Absolute(pinned)), "Pinned content must survive cleanup.");
        Assert.True(File.Exists(Absolute(active)), "The active wallpaper must survive cleanup.");

        var staleRow = await _repository.GetByIdAsync(stale.Id);
        var pinnedRow = await _repository.GetByIdAsync(pinned.Id);
        var activeRow = await _repository.GetByIdAsync(active.Id);

        Assert.NotNull(staleRow);
        Assert.NotNull(pinnedRow);
        Assert.NotNull(activeRow);
        Assert.False(staleRow.IsCached);
        Assert.True(pinnedRow.IsCached);
        Assert.True(activeRow.IsCached);
    }

    [Fact]
    public async Task EnsureSpaceAsync_ReportsInsufficientWhenOnlyProtectedContentRemains()
    {
        await SeedAsync("stale", 2 * Mb, lastAccessTicks: 1_000);
        await SeedAsync("staler", 4 * Mb, lastAccessTicks: 2_000);
        var pinned = await SeedAsync("pinned", 4 * Mb, lastAccessTicks: 3_000, pinned: true);
        var active = await SeedAsync("active", 4 * Mb, lastAccessTicks: 4_000);

        var cache = CreateCache(limitBytes: 10 * Mb);
        var result = await cache.EnsureSpaceAsync(3 * Mb, active.Id);

        Assert.False(result.EnoughSpace);
        Assert.Contains("Cannot free enough space", result.Message);
        Assert.True(File.Exists(Absolute(pinned)));
        Assert.True(File.Exists(Absolute(active)));
    }

    [Fact]
    public async Task EnsureSpaceAsync_EvictsFavoritesLast()
    {
        // The favorite is the less recently used of the two, but favourites rank
        // behind plain items in the eviction order.
        var plain = await SeedAsync("plain", 2 * Mb, lastAccessTicks: 1_000);
        var favorite = await SeedAsync("favorite", 2 * Mb, lastAccessTicks: 500, favorite: true);

        var cache = CreateCache(limitBytes: 3 * Mb);
        var result = await cache.EnsureSpaceAsync(1 * Mb, activeWallpaperId: null);

        Assert.True(result.EnoughSpace);
        Assert.False(File.Exists(Absolute(plain)));
        Assert.True(File.Exists(Absolute(favorite)), "Favorites are evicted only after unfavourited items.");
    }

    [Fact]
    public async Task EnsureSpaceAsync_EvictsFavoritesAsALastResort()
    {
        // Favouriting lowers eviction priority; it does not make an item immune.
        // Pinned and active content is what is immune (see the protection test).
        var plain = await SeedAsync("plain", 2 * Mb, lastAccessTicks: 1_000);
        var favorite = await SeedAsync("favorite", 2 * Mb, lastAccessTicks: 500, favorite: true);

        var cache = CreateCache(limitBytes: 1 * Mb);
        var result = await cache.EnsureSpaceAsync(1 * Mb, activeWallpaperId: null);

        Assert.True(result.EnoughSpace);
        Assert.False(File.Exists(Absolute(plain)));
        Assert.False(File.Exists(Absolute(favorite)));
    }

    [Theory]
    [InlineData("../../outside.png")]
    [InlineData("..\\..\\outside.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\wallpaper.png")]
    public void ResolveCachePath_RejectsEscapingAndRootedPaths(string relative)
    {
        var cache = CreateCache(limitBytes: 10 * Mb);
        Assert.Null(cache.ResolveCachePath(relative));
    }

    [Fact]
    public void ResolveCachePath_AllowsInnerTraversalThatStaysInsideRoot()
    {
        var cache = CreateCache(limitBytes: 10 * Mb);
        var resolved = cache.ResolveCachePath("a/b/../b/wallpaper.png");

        Assert.NotNull(resolved);
        Assert.StartsWith(_cacheRoot, resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PromoteToCacheAsync_RejectsEscapeAndStaysContained()
    {
        var cache = CreateCache(limitBytes: 10 * Mb);
        var staged = Path.Combine(_root, "staged.bin");
        File.WriteAllBytes(staged, new byte[64]);

        var escape = await cache.PromoteToCacheAsync(staged, "../escaped.png");
        Assert.False(escape.Success);
        Assert.Contains("escapes the cache root", escape.Error);

        var ok = await cache.PromoteToCacheAsync(staged, "media/escaped.png");
        Assert.True(ok.Success);
        Assert.True(File.Exists(Path.Combine(_cacheRoot, "media", "escaped.png")));
    }

    [Fact]
    public async Task RemoveFromCacheAsync_RefusesPinnedAndActive()
    {
        var pinned = await SeedAsync("pinned", 2 * Mb, lastAccessTicks: 1, pinned: true);
        var active = await SeedAsync("active", 2 * Mb, lastAccessTicks: 2);
        var cache = CreateCache(limitBytes: 10 * Mb);

        var pinnedResult = await cache.RemoveFromCacheAsync(pinned, activeWallpaperId: null);
        var activeResult = await cache.RemoveFromCacheAsync(active, active.Id);

        Assert.False(pinnedResult.Success);
        Assert.False(activeResult.Success);
        Assert.True(File.Exists(Absolute(pinned)));
        Assert.True(File.Exists(Absolute(active)));
    }

    private CacheManager CreateCache(long limitBytes) =>
        new(_repository, new FixedPolicy(_cacheRoot, limitBytes), NullLogger<CacheManager>.Instance);

    private async Task<Wallpaper> SeedAsync(
        string title,
        long bytes,
        long lastAccessTicks,
        bool pinned = false,
        bool favorite = false)
    {
        var relative = $"media/{title}.png";
        var absolute = Path.Combine(_cacheRoot, "media", $"{title}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllBytes(absolute, new byte[bytes]);

        return await _repository.AddAsync(new Wallpaper
        {
            Title = title,
            CacheRelativePath = relative,
            FileSizeBytes = bytes,
            IsCached = true,
            IsPinned = pinned,
            IsFavorite = favorite,
            CacheLastAccessTicks = lastAccessTicks,
            Kind = WallpaperKind.Static,
            Format = WallpaperFormat.Png,
        });
    }

    private string Absolute(Wallpaper wallpaper) => Path.Combine(_cacheRoot, wallpaper.CacheRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private sealed record FixedPolicy(string CacheRoot, long LimitBytes) : ICachePolicy
    {
        public bool AutoCleanup => true;
    }
}
