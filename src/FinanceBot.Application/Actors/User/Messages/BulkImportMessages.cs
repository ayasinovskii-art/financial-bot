namespace FinanceBot.Application.Actors.User.Messages;

public sealed record BulkExpensesResult(
    Guid UserId,
    int Added,
    int Skipped);

public sealed record BulkExpensesRejected(
    Guid UserId,
    string Reason);
