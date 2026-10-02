namespace Deskverse.Application;

using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Live cache policy snapshot derived from persisted user preferences. Refreshed
/// whenever preferences are saved so the cache manager always sees current policy.
/// </summary>
public sealed class PreferencesCachePolicy : ICachePolicy
{
    private readonly IPreferencesStore _preferencesStore;
    private readonly IAppEnvironment _environment;
    private readonly ILogger<PreferencesCachePolicy> _logger;
    private readonly object _gate = new();

    private string _cacheRoot;
    private long _limitBytes;
    private bool _autoCleanup;

    public PreferencesCachePolicy(
        IPreferencesStore preferencesStore,
        IAppEnvironment environment,
        ILogger<PreferencesCachePolicy> logger)
    {
        _preferencesStore = preferencesStore;
        _environment = environment;
        _logger = logger;

        var preferences = preferencesStore.LoadAsync().GetAwaiter().GetResult();
        _cacheRoot = ResolveRoot(preferences);
        _limitBytes = preferences.StorageLimitBytes;
        _autoCleanup = preferences.AutoCleanupEnabled;
    }

    public string CacheRoot
    {
        get
        {
            lock (_gate)
            {
                return _cacheRoot;
            }
        }
    }

    public long LimitBytes
    {
        get
        {
            lock (_gate)
            {
                return _limitBytes;
            }
        }
    }

    public bool AutoCleanup
    {
        get
        {
            lock (_gate)
            {
                return _autoCleanup;
            }
        }
    }

    /// <summary>Called by the preferences service after a save so policy follows settings immediately.</summary>
    public void Refresh(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        lock (_gate)
        {
            _cacheRoot = ResolveRoot(preferences);
            _limitBytes = preferences.StorageLimitBytes;
            _autoCleanup = preferences.AutoCleanupEnabled;
        }

        _logger.LogInformation(
            "Cache policy refreshed: root={Root}, limit={LimitBytes:N0} bytes, autoCleanup={AutoCleanup}",
            _cacheRoot,
            _limitBytes,
            _autoCleanup);
    }

    private string ResolveRoot(UserPreferences preferences)
    {
        if (!string.IsNullOrWhiteSpace(preferences.CacheDirectory))
        {
            return Path.GetFullPath(preferences.CacheDirectory);
        }

        // Before first-run setup the default managed cache is used so the app works immediately.
        return _environment.DefaultCacheDirectory;
    }
}
