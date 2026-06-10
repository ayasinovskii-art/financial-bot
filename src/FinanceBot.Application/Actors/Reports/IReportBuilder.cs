namespace FinanceBot.Application.Actors.Reports;

/// <summary>
/// Чтение данных + сборка текстового отчёта за указанный период (current, previous, N назад).
/// Реализация — ReportBuilder в Infrastructure.
/// </summary>
public interface IReportBuilder
{
    /// <summary>
    /// Параметр <paramref name="periodsAgo"/>: 0 = current (active), 1 = previous, 2..N — глубже в историю.
    /// </summary>
    Task<ReportResult> BuildAsync(Guid userId, int periodsAgo, CancellationToken ct);

    /// <summary>Сводка /stats: топ категорий трат за период (см. <see cref="StatsTextBuilder"/>).</summary>
    Task<ReportResult> BuildStatsAsync(Guid userId, int periodsAgo, CancellationToken ct);
}

/// <summary>Результат сборки текстового отчёта.</summary>
public sealed record ReportResult(bool HasData, string Text);
