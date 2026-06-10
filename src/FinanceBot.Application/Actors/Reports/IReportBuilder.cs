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

    /// <summary>CSV-выгрузка трат периода для /export (см. <see cref="ExpensesCsvBuilder"/>).</summary>
    Task<ExportResult> ExportExpensesAsync(Guid userId, int periodsAgo, CancellationToken ct);
}

/// <summary>Результат сборки текстового отчёта.</summary>
public sealed record ReportResult(bool HasData, string Text);

/// <summary>Результат CSV-выгрузки: документ для отправки или HasData=false (нечего выгружать).</summary>
public sealed record ExportResult(bool HasData, string FileName, byte[] Content);
