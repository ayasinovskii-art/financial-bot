namespace FinanceBot.Application.Actors.Telegram;

/// <summary>Строка расхода для отображения в списке удаления.</summary>
public sealed record DeleteExpenseRow(Guid Id, decimal Amount, string Description, DateTimeOffset OccurredAt);

/// <summary>Строка дохода для отображения в списке удаления.</summary>
public sealed record DeleteIncomeRow(Guid Id, decimal Amount, string? Description, DateTimeOffset OccurredAt);

/// <summary>Read-only reader для построения постраничных списков удаления.</summary>
public interface IDeleteListReader
{
    Task<IReadOnlyList<DeleteExpenseRow>> GetLastExpensesAsync(Guid userId, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<DeleteIncomeRow>> GetLastIncomesAsync(Guid userId, int skip, int take, CancellationToken ct);
    Task<DeleteExpenseRow?> GetExpenseAsync(Guid userId, Guid id, CancellationToken ct);
    Task<DeleteIncomeRow?> GetIncomeAsync(Guid userId, Guid id, CancellationToken ct);
}
