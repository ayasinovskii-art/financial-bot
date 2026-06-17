namespace FinanceBot.Application.Projections;

/// <summary>Запись доходов в app.incomes.</summary>
public interface IIncomeReadModelWriter
{
    Task InsertAsync(
        Guid incomeId,
        Guid userId,
        Guid periodId,
        decimal amount,
        DateTimeOffset occurredAt,
        string? description,
        DateTimeOffset createdAt,
        CancellationToken ct);

    Task DeleteAsync(Guid incomeId, CancellationToken ct);
}
