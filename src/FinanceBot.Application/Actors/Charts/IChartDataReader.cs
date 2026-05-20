using FinanceBot.Domain.Events.Reports;

namespace FinanceBot.Application.Actors.Charts;

/// <summary>
/// Чтение исходных данных для рендеринга графика из read-model.
/// Реализация — ChartDataReader в Infrastructure.
/// </summary>
public interface IChartDataReader
{
    Task<ChartDataSet?> LoadAsync(Guid userId, ChartType type, CancellationToken ct);
}

/// <summary>
/// Низкоуровневый чистый рендерер PNG. Не знает Akka — может вызываться синхронно из пула рендереров.
/// </summary>
public interface IChartRenderer
{
    byte[] Render(ChartDataSet data, int width = 1024, int height = 640);
}
