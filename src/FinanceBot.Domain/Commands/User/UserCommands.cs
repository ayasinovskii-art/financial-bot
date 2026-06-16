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

/// <summary>Запросить топ категорий трат за период (read-only сводка).</summary>
public sealed record RequestStats(
    Guid UserId,
    string? Period) : IUserScopedCommand;

/// <summary>Запросить CSV-выгрузку трат периода (read-only).</summary>
public sealed record RequestExport(
    Guid UserId,
    string? Period) : IUserScopedCommand;

/// <summary>Отменить активный диалог (FSM в Idle).</summary>
public sealed record Cancel(Guid UserId) : IUserScopedCommand;

/// <summary>Привязать или обновить last-known chatId. Идемпотентно — UserActor персистирует событие только при смене chatId.</summary>
public sealed record LinkUserChat(Guid UserId, long ChatId) : IUserScopedCommand;

/// <summary>Добавить финансовую цель с текстовым описанием и опциональными суммой/датой.</summary>
public sealed record AddGoal(
    Guid UserId,
    Guid GoalId,
    string Description,
    decimal? TargetAmount,
    DateOnly? TargetDate) : IUserScopedCommand;

/// <summary>Отметить цель достигнутой.</summary>
public sealed record CompleteGoal(
    Guid UserId,
    Guid GoalId) : IUserScopedCommand;

/// <summary>Массово добавить траты из импорта (CSV). Дедупликация по Amount+Date+Description выполняется в UserActor.</summary>
public sealed record BulkAddExpenses(
    Guid UserId,
    Guid PeriodId,
    IReadOnlyList<BulkExpenseRow> Rows) : IUserScopedCommand;
