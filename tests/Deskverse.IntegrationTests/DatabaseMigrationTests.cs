namespace Deskverse.IntegrationTests;

using Deskverse.Core;
using Deskverse.Core.Entities;
using Deskverse.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Xunit;

/// <summary>
/// Exercises the real migration pipeline against SQLite. The app applies pending
/// migrations on every launch, so a migration that does not run — or that runs in
/// the wrong order — is a startup crash rather than a deferred bug.
/// </summary>
public sealed class DatabaseMigrationTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;

    public DatabaseMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dv-migrations-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _provider = new ServiceCollection()
            .AddDbContextFactory<DeskverseDbContext>(options => options.UseSqlite($"Data Source={DatabasePath}"))
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp sandbox.
        }
    }

    private string DatabasePath => Path.Combine(_root, "migrate.db");

    private IDbContextFactory<DeskverseDbContext> Factory =>
        _provider.GetRequiredService<IDbContextFactory<DeskverseDbContext>>();

    [Fact]
    public async Task MigrateFromEmptyDatabase_CreatesEveryMappedTable()
    {
        var factory = Factory;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
        }

        var tables = ReadNames("SELECT name FROM sqlite_master WHERE type='table'");

        Assert.Contains("Wallpapers", tables);
        Assert.Contains("Preferences", tables);
        Assert.Contains("Collections", tables);
        Assert.Contains("CollectionItems", tables);
        Assert.Contains("WallpaperUsage", tables);
        Assert.Contains("ProviderConfigurations", tables);
    }

    [Fact]
    public async Task MigrateFromEmptyDatabase_CreatesEveryMappedColumn()
    {
        var factory = Factory;
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        // Columns the model knows about but the schema does not are silent data-loss
        // at runtime: the entity materializes, then the next SELECT throws.
        AssertMissingColumns(db.Model, "Wallpapers");
        AssertMissingColumns(db.Model, "Preferences");
        AssertMissingColumns(db.Model, "Collections");
        AssertMissingColumns(db.Model, "CollectionItems");
        AssertMissingColumns(db.Model, "WallpaperUsage");
        AssertMissingColumns(db.Model, "ProviderConfigurations");
    }

    [Fact]
    public async Task MigrateTwice_IsIdempotent()
    {
        var factory = Factory;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            await db.Database.MigrateAsync();
        }

        using var connection = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
        using var reader = await command.ExecuteReaderAsync();

        var applied = new List<string>();
        while (await reader.ReadAsync())
        {
            applied.Add(reader.GetString(0));
        }

        Assert.Collection(applied,
            id => Assert.EndsWith("InitialCreate", id, StringComparison.Ordinal),
            id => Assert.EndsWith("AddRotationFields", id, StringComparison.Ordinal),
            id => Assert.EndsWith("StoreTimestampsAsUtc", id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MigrateUpgradesLegacyOffsetTimestamps()
    {
        var factory = Factory;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync("20261002115217_InitialCreate");
        }

        // Exactly what the pre-converter model wrote.
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO Wallpapers (Id, Title, CacheRelativePath, FileHash, Kind, Format, Width, Height,
                    FileSizeBytes, Categories, CreatedAt, IsFavorite, IsPinned, IsCached, SafetyStatus,
                    IsUserImported, CacheLastAccessTicks, UseCount, IsDisliked)
                VALUES ('11111111-1111-1111-1111-111111111111', 'legacy', 'media/a.png', 'hash',
                    0, 0, 1920, 1080, 10, '[]', '2026-10-03 09:21:18.4792076+00:00',
                    0, 0, 0, 0, 1, 0, 0, 0);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();

            var legacy = await db.Wallpapers.AsNoTracking()
                .SingleAsync(w => w.Title == "legacy");

            Assert.Equal(
                DateTime.Parse("2026-10-03 09:21:18.4792076", CultureInfo.InvariantCulture),
                legacy.CreatedAt.UtcDateTime);
        }
    }

    [Fact]
    public async Task RotationColumnsRoundTrip()
    {
        var factory = Factory;
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();

            var preferences = new UserPreferences
            {
                RotationEnabled = true,
                RotationIntervalSeconds = 7200,
                RotationMode = RotationMode.Sequential,
            };
            db.Preferences.Add(preferences);
            await db.SaveChangesAsync();
        }

        await using (var reloaded = await factory.CreateDbContextAsync())
        {
            var stored = await reloaded.Preferences.AsNoTracking().SingleAsync();
            Assert.True(stored.RotationEnabled);
            Assert.Equal(7200, stored.RotationIntervalSeconds);
            Assert.Equal(RotationMode.Sequential, stored.RotationMode);
        }
    }

    private HashSet<string> ReadNames(string sql, int ordinal = 0)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            names.Add(reader.GetString(ordinal));
        }

        return names;
    }

    private void AssertMissingColumns(IModel model, string tableName)
    {
        var entityType = model.GetEntityTypes()
            .Single(candidate => candidate.GetTableName() == tableName);

        var actual = ReadNames($"PRAGMA table_info(\"{tableName}\")", ordinal: 1);
        var missing = entityType.GetProperties()
            .Select(property => property.GetColumnName())
            .Where(column => !actual.Contains(column))
            .ToList();

        Assert.Empty(missing);
    }
}
