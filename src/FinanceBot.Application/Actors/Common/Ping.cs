namespace FinanceBot.Application.Actors.Common;

/// <summary>Универсальный health-ping. Используется в тестах и для прогрева акторов.</summary>
public sealed record Ping;

/// <summary>Ответ на <see cref="Ping"/>.</summary>
public sealed record Pong(string ActorPath);
