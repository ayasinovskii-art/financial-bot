using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Commands.User;

/// <summary>Зарегистрировать пользователя (первый /start у whitelisted).</summary>
public sealed record RegisterUser(
    Guid UserId,
    long TelegramId,
    string Timezone) : IUserScopedCommand;

/// <summary>Изменить per-user настройку. Value хранится как сериализованный JSON.</summary>
public sealed record UpdateSettings(
    Guid UserId,
    SettingsKey Key,
    string Value) : IUserScopedCommand;

/// <summary>Сбросить одну или все настройки.</summary>
public sealed record ResetSettings(
    Guid UserId,
    SettingsKey? Key) : IUserScopedCommand;

public sealed record ReportIncome(
    Guid UserId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Description) : IUserScopedCommand;

public sealed record ReportExpense(
    Guid UserId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string Description,
    ExpenseSource Source) : IUserScopedCommand;

public sealed record CorrectExpenseCategory(
    Guid UserId,
    Guid ExpenseId,
    Category NewCategory) : IUserScopedCommand;

public sealed record DeleteExpense(
    Guid UserId,
    Guid ExpenseId,
    string Reason) : IUserScopedCommand;

public sealed record ConfirmSavings(
    Guid UserId,
    Guid PeriodId,
    decimal Amount) : IUserScopedCommand;

public sealed record RequestConsultation(
    Guid UserId,
    string Prompt,
    string? Scope) : IUserScopedCommand;

public sealed record RequestChart(
    Guid UserId,
    string ChartType,
    string? Params) : IUserScopedCommand;

public sealed record RequestReport(
    Guid UserId,
    string? Period) : IUserScopedCommand;

/// <summary>Отменить активный диалог (FSM в Idle).</summary>
public sealed record Cancel(Guid UserId) : IUserScopedCommand;
