namespace Deskverse.Infrastructure.Repositories;

using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>Persists the single user-preferences row, creating it on first load.</summary>
public sealed class PreferencesStore : IPreferencesStore
{
    private readonly IDbContextFactory<DeskverseDbContext> _contextFactory;

    public PreferencesStore(IDbContextFactory<DeskverseDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<UserPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var preferences = await db.Preferences.FirstOrDefaultAsync(p => p.Id == UserPreferences.SingletonId, cancellationToken);
        if (preferences is not null)
        {
            return preferences;
        }

        preferences = new UserPreferences();
        db.Preferences.Add(preferences);
        await db.SaveChangesAsync(cancellationToken);
        return preferences;
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        preferences.Id = UserPreferences.SingletonId;
        preferences.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Preferences.FirstOrDefaultAsync(p => p.Id == UserPreferences.SingletonId, cancellationToken);
        if (existing is null)
        {
            db.Preferences.Add(preferences);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(preferences);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsFirstRunCompleteAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var preferences = await db.Preferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == UserPreferences.SingletonId, cancellationToken);
        return preferences?.FirstRunComplete == true;
    }

    public async Task MarkFirstRunCompleteAsync(CancellationToken cancellationToken = default)
    {
        var preferences = await LoadAsync(cancellationToken);
        preferences.FirstRunComplete = true;
        await SaveAsync(preferences, cancellationToken);
    }
}
