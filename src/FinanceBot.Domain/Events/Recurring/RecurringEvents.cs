using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Events.Recurring;

/// <summary>Создан шаблон регулярной траты.</summary>
public sealed record RecurringTemplateAdded(
    Guid UserId,
    Guid TemplateId,
    string Name,
    decimal Amount,
    ScheduleSpec Schedule,
    Category? Category,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Удалён шаблон.</summary>
public sealed record RecurringTemplateRemoved(
    Guid UserId,
    Guid TemplateId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Бот ожидает регулярную трату по шаблону на указанную дату (для аналитики).</summary>
public sealed record RecurringExpenseExpected(
    Guid UserId,
    Guid TemplateId,
    DateOnly Date,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Регулярная трата авто-зафиксирована при молчании пользователя.</summary>
public sealed record RecurringExpenseAutoConfirmed(
    Guid UserId,
    Guid TemplateId,
    Guid ExpenseId,
    DateOnly Date,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
