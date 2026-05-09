namespace FinanceBot.Domain.Events;

/// <summary>
/// Маркер доменного события. Все доменные события — иммутабельные records.
/// Каждое событие имеет EventVersion: int = 1; при изменении схемы — новая версия + IEventAdapter.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Версия схемы события. Default = 1.</summary>
    int EventVersion { get; }

    /// <summary>UTC-время доменного события (когда оно произошло, не когда сохранилось).</summary>
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Событие, привязанное к конкретному пользователю. Используется для тагирования event journal по userId.
/// </summary>
public interface IUserScopedEvent : IDomainEvent
{
    Guid UserId { get; }
}
