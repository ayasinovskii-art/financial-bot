namespace FinanceBot.Application.Actors.Advisor;

/// <summary>
/// Чтение AdvisorSnapshot из read-model (app.periods, app.expenses, app.incomes).
/// Реализация — AdvisorSnapshotReader в Infrastructure.
/// </summary>
public interface IAdvisorSnapshotReader
{
    Task<AdvisorSnapshot> BuildAsync(Guid userId, DateTimeOffset now, CancellationToken ct);
}
