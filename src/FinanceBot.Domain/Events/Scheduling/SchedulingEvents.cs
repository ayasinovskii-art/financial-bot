namespace FinanceBot.Domain.Events.Scheduling;

/// <summary>Сработал вечерний тик.</summary>
public sealed record EveningTickFired(
    Guid UserId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Сработал тик дедлайна молчания (через silence_deadline_hours после EveningTick).</summary>
public sealed record SilenceDeadlineFired(
    Guid UserId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Сработал тик дня зарплаты.</summary>
public sealed record SalaryDayTickFired(
    Guid UserId,
    int SalaryDay,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Сработал тик еженедельного советника.</summary>
public sealed record WeeklyAdvisorTickFired(
    Guid UserId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Сработал тик ежемесячного советника.</summary>
public sealed record MonthlyAdvisorTickFired(
    Guid UserId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Бот задал пользователю вечерний вопрос (для аналитики и backfill).</summary>
public sealed record EveningQuestionAsked(
    Guid UserId,
    DateOnly Date,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Сработал воскресный тик еженедельного дайджеста.</summary>
public sealed record WeeklyDigestTickFired(
    Guid UserId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
