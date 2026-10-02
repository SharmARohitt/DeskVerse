namespace Deskverse.Infrastructure.Persistence;

using Deskverse.Core.Entities;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The SQLite data model. Cache paths are relative strings, hashes are plain hex,
/// and array properties are stored as JSON columns (EF Core primitive collections).
/// </summary>
public sealed class DeskverseDbContext : DbContext
{
    public DeskverseDbContext(DbContextOptions<DeskverseDbContext> options)
        : base(options)
    {
    }

    public DbSet<Wallpaper> Wallpapers => Set<Wallpaper>();

    public DbSet<WallpaperUsage> WallpaperUsage => Set<WallpaperUsage>();

    public DbSet<UserPreferences> Preferences => Set<UserPreferences>();

    public DbSet<ProviderConfiguration> ProviderConfigurations => Set<ProviderConfiguration>();

    public DbSet<WallpaperCollection> Collections => Set<WallpaperCollection>();

    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallpaper>(entity =>
        {
            entity.HasIndex(w => w.FileHash);
            entity.HasIndex(w => new { w.SourceProvider, w.SourceId });
            entity.HasIndex(w => new { w.IsCached, w.CacheLastAccessTicks });
            entity.HasIndex(w => w.LastUsedAt);
            entity.HasIndex(w => w.IsFavorite);

            entity.HasMany(w => w.Usage)
                .WithOne(u => u.Wallpaper!)
                .HasForeignKey(u => u.WallpaperId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(w => w.Title).HasMaxLength(300);
            entity.Property(w => w.FileHash).HasMaxLength(64).IsRequired();
            entity.Property(w => w.CacheRelativePath).HasMaxLength(400);
        });

        modelBuilder.Entity<WallpaperUsage>(entity =>
        {
            entity.HasIndex(u => new { u.WallpaperId, u.AppliedAt });
            entity.HasIndex(u => u.AppliedAt);
        });

        modelBuilder.Entity<UserPreferences>(entity =>
        {
            // Single-row table: fixed key, no identity.
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.Property(p => p.CacheDirectory).HasMaxLength(600);
        });

        modelBuilder.Entity<ProviderConfiguration>(entity =>
        {
            entity.HasKey(p => p.ProviderId);
            entity.Property(p => p.ProviderId).HasMaxLength(100);
            entity.Property(p => p.LastError).HasMaxLength(2000);
        });

        modelBuilder.Entity<WallpaperCollection>(entity =>
        {
            entity.HasIndex(c => c.Name);
            entity.Property(c => c.Name).HasMaxLength(200);
        });

        // A wallpaper appears at most once per collection.
        modelBuilder.Entity<CollectionItem>(entity =>
        {
            entity.HasKey(i => new { i.CollectionId, i.WallpaperId });
            entity.HasIndex(i => i.WallpaperId);

            entity.HasOne(i => i.Collection!)
                .WithMany(c => c.Items)
                .HasForeignKey(i => i.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.Wallpaper)
                .WithMany()
                .HasForeignKey(i => i.WallpaperId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
