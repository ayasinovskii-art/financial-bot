namespace FinanceBot.Infrastructure.Persistence.Entities;

/// <summary>Read-model записи whitelist. Заполняется WhitelistProjection.</summary>
public sealed class WhitelistEntity
{
    public long TelegramId { get; set; }
    public long AddedBy { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
