namespace FinanceBot.Application.Projections;

/// <summary>
/// Хранилище offset-ов проекций (по одной строке на projection name в таблице app.projection_offsets).
/// Реализация — в Infrastructure.
/// </summary>
public interface IProjectionOffsetStore
{
    Task<long> LoadAsync(string projectionName, CancellationToken ct);
    Task SaveAsync(string projectionName, long offset, CancellationToken ct);
}
