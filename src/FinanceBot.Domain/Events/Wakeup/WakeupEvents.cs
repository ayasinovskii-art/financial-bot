using System.Collections.Immutable;

namespace FinanceBot.Domain.Events.Wakeup;

/// <summary>Бот обнаружил простой больше порога (по разрыву в SystemHeartbeat).</summary>
public sealed record SystemDowntimeDetected(
    DateTimeOffset From,
    DateTimeOffset To,
    long DurationSeconds,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IDomainEvent;

/// <summary>Пользователю отправлено wakeup-уведомление с перечислением пропущенного.</summary>
public sealed record WakeupNotificationSent(
    Guid UserId,
    ImmutableArray<string> MissedItems,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
