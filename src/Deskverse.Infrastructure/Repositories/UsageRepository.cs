namespace Deskverse.Infrastructure.Repositories;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class UsageRepository : IUsageRepository
{
    private readonly IDbContextFactory<DeskverseDbContext> _contextFactory;

    public UsageRepository(IDbContextFactory<DeskverseDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(WallpaperUsage usage, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        db.WallpaperUsage.Add(usage);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WallpaperUsage>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.WallpaperUsage
            .AsNoTracking()
            .OrderByDescending(u => u.AppliedAt)
            .Take(Math.Clamp(count, 1, 500))
            .ToListAsync(cancellationToken);
    }

    public async Task CloseOpenUsageAsync(
        Guid wallpaperId,
        DateTimeOffset endedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var open = await db.WallpaperUsage
            .Where(u => u.WallpaperId == wallpaperId && u.DurationSeconds == 0)
            .OrderByDescending(u => u.AppliedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (open is null)
        {
            return;
        }

        var duration = (long)Math.Max(0, (endedAtUtc - open.AppliedAt).TotalSeconds);
        await db.WallpaperUsage
            .Where(u => u.Id == open.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(u => u.DurationSeconds, duration),
                cancellationToken);
    }

    public async Task SetFeedbackAsync(
        Guid usageId,
        UserFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.WallpaperUsage
            .Where(u => u.Id == usageId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(u => u.UserFeedback, feedback),
                cancellationToken);
    }
}
