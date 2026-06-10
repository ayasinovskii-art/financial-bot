using System.Globalization;
using System.Text;

namespace FinanceBot.Application.Actors.Reports;

/// <summary>Одна трата для CSV-выгрузки /export.</summary>
public sealed record ExpenseCsvRow(DateOnly Date, decimal Amount, string Category, string? Description);

/// <summary>
/// Чистый билдер CSV для /export (RFC 4180): заголовок + строка на трату,
/// поля с запятой/кавычкой/переводом строки оборачиваются в кавычки, кавычки удваиваются.
/// Данные загружает <see cref="IReportBuilder"/> в Infrastructure; здесь — только текст.
/// </summary>
public static class ExpensesCsvBuilder
{
    public static string Build(IReadOnlyList<ExpenseCsvRow> rows)
    {
        var sb = new StringBuilder(64 + rows.Count * 48);
        sb.Append("date,amount,category,description");
        foreach (var row in rows)
        {
            sb.Append("\r\n");
            sb.Append(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.Amount.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(Escape(row.Category));
            sb.Append(',');
            sb.Append(Escape(row.Description ?? string.Empty));
        }
        return sb.ToString();
    }

    private static string Escape(string field)
    {
        if (field.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return field;
        }
        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
