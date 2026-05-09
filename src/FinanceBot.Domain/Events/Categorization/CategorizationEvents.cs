using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Events.Categorization;

/// <summary>Запрошена категоризация (отправлен запрос в CategorizerActor / Claude).</summary>
public sealed record CategorizationRequested(
    Guid UserId,
    Guid ExpenseId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Категоризация успешно завершена.</summary>
public sealed record CategorizationCompleted(
    Guid UserId,
    Guid ExpenseId,
    Guid CorrelationId,
    Category Category,
    ExpenseSource Source,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Категоризация не удалась (обычно — Claude недоступен и no-rule).</summary>
public sealed record CategorizationFailed(
    Guid UserId,
    Guid ExpenseId,
    Guid CorrelationId,
    string Reason,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
