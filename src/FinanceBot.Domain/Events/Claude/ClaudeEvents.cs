namespace FinanceBot.Domain.Events.Claude;

/// <summary>Тип use case для запроса в Claude (для observability).</summary>
public enum ClaudeUseCase
{
    Categorization = 1,
    Advice = 2,
    StatementExtraction = 3,
    Other = 99
}

/// <summary>Тип ошибки, приведшей к unavailability.</summary>
public enum ClaudeUnavailabilityReason
{
    None = 0,
    TokensExhausted = 1,
    RateLimited = 2,
    TransientError = 3,
    Timeout = 4,
    Overloaded = 5,
    Other = 99
}

/// <summary>Отправлен запрос в Claude.</summary>
public sealed record ClaudeRequestSent(
    ClaudeUseCase UseCase,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IDomainEvent;

/// <summary>Получен ответ от Claude.</summary>
public sealed record ClaudeResponseReceived(
    Guid CorrelationId,
    long LatencyMs,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IDomainEvent;

/// <summary>Ошибка при запросе в Claude.</summary>
public sealed record ClaudeRequestFailed(
    Guid CorrelationId,
    string Reason,
    ClaudeUnavailabilityReason ErrorType,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IDomainEvent;

/// <summary>Claude переведён в состояние Unavailable до указанного времени.</summary>
public sealed record ClaudeBecameUnavailable(
    ClaudeUnavailabilityReason Reason,
    DateTimeOffset Until,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IDomainEvent;

/// <summary>Claude снова доступен.</summary>
public sealed record ClaudeBecameAvailable(
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IDomainEvent;
