namespace Deskverse.Application;

using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Preference persistence plus the first-run storage setup flow. Saving
/// preferences immediately refreshes the live cache policy.
/// </summary>
public sealed class PreferencesService
{
    private readonly IPreferencesStore _store;
    private readonly PreferencesCachePolicy _cachePolicy;
    private readonly ILogger<PreferencesService> _logger;

    public PreferencesService(
        IPreferencesStore store,
        PreferencesCachePolicy cachePolicy,
        ILogger<PreferencesService> logger)
    {
        _store = store;
        _cachePolicy = cachePolicy;
        _logger = logger;
    }

    public Task<UserPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
        _store.LoadAsync(cancellationToken);

    public async Task<OperationResult> SaveAsync(
        UserPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (preferences.StorageLimitBytes is < 512L * 1024 * 1024 or > 4L * 1024 * 1024 * 1024)
        {
            return OperationResult.Fail("The cache limit must be between 0.5 GB and 4 TB.");
        }

        if (!string.IsNullOrWhiteSpace(preferences.CacheDirectory))
        {
            var error = CacheDirectoryRules.Validate(preferences.CacheDirectory);
            if (error is not null)
            {
                return OperationResult.Fail(error);
            }
        }

        preferences.UpdatedAt = DateTimeOffset.UtcNow;
        await _store.SaveAsync(preferences, cancellationToken).ConfigureAwait(false);
        _cachePolicy.Refresh(preferences);
        _logger.LogInformation("Preferences saved and cache policy refreshed.");
        return OperationResult.Ok();
    }

    /// <summary>First-run setup: choose the managed cache folder and its size limit.</summary>
    public async Task<OperationResult> CompleteFirstRunAsync(
        string cacheDirectory,
        long storageLimitBytes,
        CancellationToken cancellationToken = default)
    {
        var validationError = CacheDirectoryRules.Validate(cacheDirectory);
        if (validationError is not null)
        {
            return OperationResult.Fail(validationError);
        }

        var prepareError = CacheDirectoryRules.TryPrepare(Path.GetFullPath(cacheDirectory.Trim()));
        if (prepareError is not null)
        {
            return OperationResult.Fail(prepareError);
        }

        var preferences = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        preferences.CacheDirectory = Path.GetFullPath(cacheDirectory.Trim());
        preferences.StorageLimitBytes = storageLimitBytes;
        preferences.UpdatedAt = DateTimeOffset.UtcNow;

        await _store.SaveAsync(preferences, cancellationToken).ConfigureAwait(false);
        await _store.MarkFirstRunCompleteAsync(cancellationToken).ConfigureAwait(false);
        _cachePolicy.Refresh(preferences);

        _logger.LogInformation(
            "First-run setup complete. Cache root: {Root}, limit {LimitBytes:N0} bytes.",
            preferences.CacheDirectory, preferences.StorageLimitBytes);
        return OperationResult.Ok();
    }

    public Task<bool> IsFirstRunCompleteAsync(CancellationToken cancellationToken = default) =>
        _store.IsFirstRunCompleteAsync(cancellationToken);
}
