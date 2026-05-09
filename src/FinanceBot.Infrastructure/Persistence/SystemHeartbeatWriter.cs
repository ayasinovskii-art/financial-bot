using FinanceBot.Application.Projections;
using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Persistence;

public sealed class SystemHeartbeatWriter(IDbContextFactory<AppDbContext> dbFactory) : ISystemHeartbeatWriter
{
    public async Task UpsertAsync(DateTimeOffset lastSeen, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.SystemHeartbeat.FindAsync([1], ct);
        if (existing is null)
        {
            db.SystemHeartbeat.Add(new SystemHeartbeatEntity { Id = 1, LastSeen = lastSeen });
        }
        else
        {
            existing.LastSeen = lastSeen;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<DateTimeOffset?> ReadLastSeenAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.SystemHeartbeat.AsNoTracking().FirstOrDefaultAsync(ct);
        return row?.LastSeen;
    }
}
