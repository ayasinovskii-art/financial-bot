using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Events.Expense;

/// <summary>Пользователь зафиксировал трату. Категория ещё не определена (см. ExpenseCategorizedAutomatically).</summary>
public sealed record ExpenseReported(
    Guid UserId,
    Guid ExpenseId,
    Guid PeriodId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string Description,
    ExpenseSource Source,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Категоризатор автоматически проставил категорию (memory / rules / claude / fallback).</summary>
public sealed record ExpenseCategorizedAutomatically(
    Guid UserId,
    Guid ExpenseId,
    Category Category,
    ExpenseSource Source,
    bool NeedsReview,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Пользователь явно подтвердил автоматически назначенную категорию (через inline-кнопку).</summary>
public sealed record ExpenseCategoryConfirmed(
    Guid UserId,
    Guid ExpenseId,
    Category Category,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Пользователь изменил категорию через /correct. Обновляет memory.</summary>
public sealed record ExpenseCategoryCorrected(
    Guid UserId,
    Guid ExpenseId,
    Category OldCategory,
    Category NewCategory,
    NormalizedDescription NormalizedDescription,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Удаление траты (например, ошибочная запись).</summary>
public sealed record ExpenseDeleted(
    Guid UserId,
    Guid ExpenseId,
    string Reason,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
