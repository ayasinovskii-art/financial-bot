namespace FinanceBot.Application.Actors.UserPlannedExpenses.Messages;

/// <summary>Read-форма запланированной траты.</summary>
public sealed record PlannedExpenseView(
    Guid PlannedId,
    decimal Amount,
    DateOnly Date,
    string Description,
    PlannedStatus Status,
    Guid? ConfirmedExpenseId);

public enum PlannedStatus
{
    Active = 0,
    Confirmed = 1,
    Cancelled = 2
}

public sealed record PlannedAdded(Guid UserId, PlannedExpenseView Plan);
public sealed record PlannedRemoved(Guid UserId, Guid PlannedId);
public sealed record PlannedConfirmed(Guid UserId, Guid PlannedId, Guid ExpenseId);
public sealed record PlannedRejected(Guid UserId, string Reason);
public sealed record PlannedList(Guid UserId, IReadOnlyList<PlannedExpenseView> Plans);
