using FinanceBot.Application.Projections;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Projections;

public sealed class WhitelistReadModelWriter(IDbContextFactory<AppDbContext> dbFactory) : IWhitelistReadModelWriter
{
    public async Task UpsertWhitelistedAsync(long telegramId, long addedBy, DateTimeOffset addedAt, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Whitelist.FirstOrDefaultAsync(x => x.TelegramId == telegramId, ct);
        if (existing is null)
        {
            db.Whitelist.Add(new WhitelistEntity
            {
                TelegramId = telegramId,
                AddedBy = addedBy,
                AddedAt = addedAt,
                RevokedAt = null
            });
        }
        else
        {
            existing.AddedBy = addedBy;
            existing.AddedAt = addedAt;
            existing.RevokedAt = null;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkRevokedAsync(long telegramId, DateTimeOffset revokedAt, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Whitelist
            .Where(x => x.TelegramId == telegramId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, (DateTimeOffset?)revokedAt), ct);
    }
}
