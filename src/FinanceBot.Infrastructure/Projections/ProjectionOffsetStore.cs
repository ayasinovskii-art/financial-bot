using FinanceBot.Application.Projections;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Projections;

/// <summary>
/// Реализация <see cref="IProjectionOffsetStore"/> поверх app.projection_offsets.
/// </summary>
public sealed class ProjectionOffsetStore(IDbContextFactory<AppDbContext> dbFactory) : IProjectionOffsetStore
{
    public async Task<long> LoadAsync(string projectionName, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.ProjectionOffsets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectionName == projectionName, ct);
        return row?.OffsetValue ?? 0L;
    }

    public async Task SaveAsync(string projectionName, long offset, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.ProjectionOffsets
            .FirstOrDefaultAsync(x => x.ProjectionName == projectionName, ct);

        if (existing is null)
        {
            db.ProjectionOffsets.Add(new ProjectionOffsetEntity
            {
                ProjectionName = projectionName,
                OffsetValue = offset,
                LastUpdated = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.OffsetValue = offset;
            existing.LastUpdated = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
