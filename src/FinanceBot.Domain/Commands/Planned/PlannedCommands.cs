namespace FinanceBot.Domain.Commands.Planned;

public sealed record AddPlanned(
    Guid UserId,
    decimal Amount,
    DateOnly Date,
    string Description) : IUserScopedCommand;

public sealed record RemovePlanned(
    Guid UserId,
    Guid PlannedId) : IUserScopedCommand;

public sealed record ConfirmPlanned(
    Guid UserId,
    Guid PlannedId,
    decimal? ActualAmount) : IUserScopedCommand;

public sealed record ListPlanned(
    Guid UserId) : IUserScopedCommand;
