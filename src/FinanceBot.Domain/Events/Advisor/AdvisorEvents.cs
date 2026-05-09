namespace FinanceBot.Domain.Events.Advisor;

/// <summary>Источник, сгенерировавший совет / консультацию.</summary>
public enum ConsultationSource
{
    Claude = 1,
    LocalHeuristics = 2
}

/// <summary>Тип advisor-тика, по которому был запрошен совет.</summary>
public enum AdvisorTickType
{
    Weekly = 1,
    Monthly = 2,
    OnDemand = 3
}

/// <summary>Запрошена консультация.</summary>
public sealed record ConsultationRequested(
    Guid UserId,
    Guid CorrelationId,
    string Prompt,
    AdvisorTickType Scope,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Консультация получена (от Claude или локальных эвристик).</summary>
public sealed record ConsultationAnswered(
    Guid UserId,
    Guid CorrelationId,
    string Response,
    ConsultationSource Source,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Запланированный совет ждёт восстановления Claude (park-and-refresh).</summary>
public sealed record AdviceParked(
    Guid UserId,
    AdvisorTickType TickType,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Запланированный совет возобновлён со свежим снапшотом данных.</summary>
public sealed record AdviceResumedWithFreshContext(
    Guid UserId,
    AdvisorTickType TickType,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
