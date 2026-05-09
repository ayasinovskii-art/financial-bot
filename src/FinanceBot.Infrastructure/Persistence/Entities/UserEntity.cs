namespace FinanceBot.Infrastructure.Persistence.Entities;

/// <summary>Read-model запись о пользователе. Заполняется UsersListProjection.</summary>
public sealed class UserEntity
{
    public Guid UserId { get; set; }
    public long TelegramId { get; set; }
    public string Timezone { get; set; } = string.Empty;
    /// <summary>JSONB. Сериализованный словарь per-user settings.</summary>
    public string SettingsJson { get; set; } = "{}";
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}
