namespace Deskverse.Infrastructure.Repositories;

using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class ProviderConfigStore : IProviderConfigStore
{
    private readonly IDbContextFactory<DeskverseDbContext> _contextFactory;

    public ProviderConfigStore(IDbContextFactory<DeskverseDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<ProviderConfiguration>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProviderConfigurations
            .AsNoTracking()
            .OrderBy(p => p.ProviderId)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(
        ProviderConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.ProviderConfigurations
            .FirstOrDefaultAsync(p => p.ProviderId == configuration.ProviderId, cancellationToken);

        if (existing is null)
        {
            db.ProviderConfigurations.Add(configuration);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(configuration);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
