namespace FinanceBot.Domain.Events.Notifications;

/// <summary>Бот отправил пользователю проактивное push-сообщение.</summary>
public sealed record ProactiveNotificationSent(
    Guid UserId,
    string TriggerKind,
    string BucketName,
    DateOnly SentDate,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
