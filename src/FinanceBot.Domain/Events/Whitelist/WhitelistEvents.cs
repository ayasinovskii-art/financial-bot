namespace FinanceBot.Domain.Events.Whitelist;

/// <summary>Администратор добавил пользователя в whitelist.</summary>
public sealed record UserWhitelisted(
    long AdminId,
    long TelegramId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IDomainEvent;

/// <summary>Администратор удалил пользователя из whitelist.</summary>
public sealed record UserRevoked(
    long AdminId,
    long TelegramId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IDomainEvent;
