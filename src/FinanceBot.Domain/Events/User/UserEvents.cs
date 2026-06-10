namespace FinanceBot.Domain.Events.User;

/// <summary>Событие первой регистрации пользователя (после успешного /start у whitelisted).</summary>
public sealed record UserRegistered(
    Guid UserId,
    long TelegramId,
    string Timezone,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Изменение per-user настройки. oldValue/newValue хранятся как сериализованный JSON.</summary>
public sealed record UserSettingsUpdated(
    Guid UserId,
    string Key,
    string? OldValue,
    string? NewValue,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Первая привязка или смена chatId пользователя. Персистируется идемпотентно — только при изменении.</summary>
public sealed record UserChatLinked(
    Guid UserId,
    long ChatId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
