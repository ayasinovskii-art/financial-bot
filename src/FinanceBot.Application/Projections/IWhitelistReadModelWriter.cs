namespace FinanceBot.Application.Projections;

/// <summary>Запись/обновление строк в app.whitelist. Реализация — WhitelistReadModelWriter в Infrastructure.</summary>
public interface IWhitelistReadModelWriter
{
    Task UpsertWhitelistedAsync(long telegramId, long addedBy, DateTimeOffset addedAt, CancellationToken ct);
    Task MarkRevokedAsync(long telegramId, DateTimeOffset revokedAt, CancellationToken ct);
}
