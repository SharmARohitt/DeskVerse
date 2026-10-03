namespace Deskverse.IntegrationTests;

using Deskverse.Application;
using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Infrastructure.Persistence;
using Deskverse.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Preference persistence against a real SQLite store, plus the validation rules
/// that guard the cache limit and cache location.
/// </summary>
public sealed class PreferencesServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;
    private readonly PreferencesStore _store;
    private readonly PreferencesCachePolicy _cachePolicy;
    private readonly PreferencesService _service;

    public PreferencesServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dv-prefs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "cache"));

        _provider = new ServiceCollection()
            .AddDbContextFactory<DeskverseDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(_root, "prefs.db")}"))
            .BuildServiceProvider();

        var factory = _provider.GetRequiredService<IDbContextFactory<DeskverseDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        _store = new PreferencesStore(factory);
        _cachePolicy = new PreferencesCachePolicy(_store, new FakeEnvironment(_root), NullLogger<PreferencesCachePolicy>.Instance);
        _service = new PreferencesService(_store, _cachePolicy, NullLogger<PreferencesService>.Instance);
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
    public async Task FreshInstall_SendsNoTelemetry()
    {
        var preferences = await _service.LoadAsync();

        Assert.False(preferences.TelemetryOptIn);
    }

    [Theory]
    [InlineData(511L * 1024 * 1024)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4L * 1024 * 1024 * 1024 * 1024 + 1)]
    public async Task SaveAsync_RejectsLimitsOutsideTheAllowedRange(long limit)
    {
        var preferences = await _service.LoadAsync();
        preferences.StorageLimitBytes = limit;

        var result = await _service.SaveAsync(preferences);

        Assert.False(result.Success);
        Assert.Contains("cache limit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SavedPreferences_RoundTripThroughTheDatabase()
    {
        var preferences = await _service.LoadAsync();
        preferences.StorageLimitBytes = 3L * 1024 * 1024 * 1024;
        preferences.PreferredCategories = ["Nature", "Minimalist"];
        preferences.PreferredColors = ["0A1B2C"];
        preferences.PreferredStyles = ["flat"];
        preferences.PreferredBrightness = 0.25;
        preferences.RotationEnabled = true;
        preferences.RotationIntervalSeconds = 7200;
        preferences.RotationMode = RotationMode.Collection;
        var collectionId = Guid.NewGuid();
        preferences.RotationCollectionId = collectionId;
        preferences.AllowVideoWallpapers = false;
        preferences.MinimizeToTray = true;

        var saved = await _service.SaveAsync(preferences);
        Assert.True(saved.Success, saved.Error);

        var reloaded = await _service.LoadAsync();
        Assert.Equal(3L * 1024 * 1024 * 1024, reloaded.StorageLimitBytes);
        Assert.Equal(["Nature", "Minimalist"], reloaded.PreferredCategories);
        Assert.Equal(["0A1B2C"], reloaded.PreferredColors);
        Assert.Equal(["flat"], reloaded.PreferredStyles);
        Assert.Equal(0.25, reloaded.PreferredBrightness!.Value);
        Assert.True(reloaded.RotationEnabled);
        Assert.Equal(7200, reloaded.RotationIntervalSeconds);
        Assert.Equal(RotationMode.Collection, reloaded.RotationMode);
        Assert.Equal(collectionId, reloaded.RotationCollectionId);
        Assert.False(reloaded.AllowVideoWallpapers);
        Assert.True(reloaded.MinimizeToTray);
    }

    [Fact]
    public async Task SavingALimit_UpdatesTheLiveCachePolicyImmediately()
    {
        var preferences = await _service.LoadAsync();
        preferences.StorageLimitBytes = 4L * 1024 * 1024 * 1024;
        preferences.CacheDirectory = Path.Combine(_root, "cache");

        Assert.True((await _service.SaveAsync(preferences)).Success);
        Assert.Equal(4L * 1024 * 1024 * 1024, _cachePolicy.LimitBytes);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(_root, "cache")),
            Path.GetFullPath(_cachePolicy.CacheRoot),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_RejectsAForbiddenCacheDirectory()
    {
        var preferences = await _service.LoadAsync();
        preferences.CacheDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var result = await _service.SaveAsync(preferences);

        Assert.False(result.Success);
        Assert.Contains("cannot live inside", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("C:\\")]
    [InlineData("C:\\Windows")]
    [InlineData("C:\\Windows\\Temp")]
    [InlineData("   ")]
    public void CacheDirectoryRules_RejectUnsafeLocations(string path)
    {
        Assert.NotNull(CacheDirectoryRules.Validate(path));
    }

    [Fact]
    public void CacheDirectoryRules_AcceptAUserSubfolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "deskverse-cache");
        Assert.Null(CacheDirectoryRules.Validate(path));
    }

    [Fact]
    public async Task CompleteFirstRunAsync_PersistsTheChosenSetup()
    {
        var target = Path.Combine(_root, "first-run-cache");
        var result = await _service.CompleteFirstRunAsync(target, 6L * 1024 * 1024 * 1024);

        Assert.True(result.Success, result.Error);

        var preferences = await _service.LoadAsync();
        Assert.True(preferences.FirstRunComplete);
        Assert.Equal(6L * 1024 * 1024 * 1024, preferences.StorageLimitBytes);
        Assert.Equal(target, preferences.CacheDirectory, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record FakeEnvironment(string Root) : IAppEnvironment
    {
        public string DataRoot => Root;
        public string DefaultCacheDirectory => Path.Combine(Root, "cache");
        public string DatabasePath => Path.Combine(Root, "prefs.db");
        public string LogsDirectory => Path.Combine(Root, "logs");
        public string ThumbnailsDirectory => Path.Combine(Root, "thumbs");
        public string TempDirectory => Path.Combine(Root, "tmp");
    }
}
