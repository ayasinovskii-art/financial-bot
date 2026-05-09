using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Commands.Templates;

public sealed record AddTemplate(
    Guid UserId,
    string Name,
    decimal Amount,
    ScheduleSpec Schedule,
    Category? Category) : IUserScopedCommand;

public sealed record RemoveTemplate(
    Guid UserId,
    string Name) : IUserScopedCommand;

public sealed record ListTemplates(
    Guid UserId) : IUserScopedCommand;
