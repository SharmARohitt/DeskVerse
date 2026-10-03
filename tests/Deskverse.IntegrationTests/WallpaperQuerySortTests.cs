namespace Deskverse.IntegrationTests;

using Deskverse.Core;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Deskverse.Infrastructure.Persistence;
using Deskverse.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Sort and timestamp behaviour of the wallpaper query pipeline against real
/// SQLite. Ordering by an instant is the interesting case: SQLite refuses to sort
/// a DateTimeOffset column, so the mapping has to store UTC date times for these
/// queries to translate at all.
/// </summary>
public sealed class WallpaperQuerySortTests : IAsyncLifetime
{
    private readonly ServiceProvider _provider;
    private readonly string _root;
    private readonly WallpaperRepository _repository;

    public WallpaperQuerySortTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dv-sort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _provider = new ServiceCollection()
            .AddDbContextFactory<DeskverseDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(_root, "sort.db")}"))
            .BuildServiceProvider();

        var factory = _provider.GetRequiredService<IDbContextFactory<DeskverseDbContext>>();
        _repository = new WallpaperRepository(factory);
    }

    public async Task InitializeAsync()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<DeskverseDbContext>>()
            .CreateDbContextAsync();
        await db.Database.MigrateAsync();

        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        await SeedAsync("oldest-used", created: now.AddDays(-3), lastUsed: now.AddDays(-2));
        await SeedAsync("middle", created: now.AddDays(-2), lastUsed: now.AddDays(-1));
        await SeedAsync("newest", created: now.AddDays(-1), lastUsed: now);
    }

    public Task DisposeAsync()
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

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(SortOrder.NewestFirst)]
    [InlineData(SortOrder.RecentlyUsed)]
    [InlineData(SortOrder.MostUsedFirst)]
    [InlineData(SortOrder.TitleAZ)]
    [InlineData(SortOrder.LargestFirst)]
    [InlineData(SortOrder.Random)]
    public async Task EverySortOrderTranslatesToSql(SortOrder order)
    {
        var page = await _repository.QueryAsync(new WallpaperQuery { SortBy = order, Take = 10 });
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task RecentlyUsedOrdersByLastUsedInstant()
    {
        var page = await _repository.QueryAsync(new WallpaperQuery
        {
            SortBy = SortOrder.RecentlyUsed,
            Take = 10,
        });

        Assert.Equal(
            ["newest", "middle", "oldest-used"],
            page.Items.Select(item => item.Title).ToArray());
    }

    [Fact]
    public async Task NewestFirstOrdersByCreatedInstant()
    {
        var page = await _repository.QueryAsync(new WallpaperQuery
        {
            SortBy = SortOrder.NewestFirst,
            Take = 10,
        });

        Assert.Equal(
            ["newest", "middle", "oldest-used"],
            page.Items.Select(item => item.Title).ToArray());
    }

    [Fact]
    public async Task TimestampsRoundTripAsUtcInstants()
    {
        var created = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var stored = await SeedAsync("round-trip", created: created, lastUsed: created.AddMinutes(30));

        var reloaded = await _repository.GetByIdAsync(stored.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(created, reloaded.CreatedAt);
        Assert.Equal(created.AddMinutes(30), reloaded.LastUsedAt);
    }

    private async Task<Wallpaper> SeedAsync(
        string title,
        DateTimeOffset created,
        DateTimeOffset? lastUsed)
    {
        await using var db = await _provider
            .GetRequiredService<IDbContextFactory<DeskverseDbContext>>()
            .CreateDbContextAsync();

        var wallpaper = new Wallpaper
        {
            Title = title,
            CacheRelativePath = $"media/{title}.png",
            FileHash = Guid.NewGuid().ToString("N"),
            CreatedAt = created,
            LastUsedAt = lastUsed,
            Kind = WallpaperKind.Static,
            Format = WallpaperFormat.Png,
            IsCached = true,
        };

        db.Wallpapers.Add(wallpaper);
        await db.SaveChangesAsync();
        return wallpaper;
    }
}
