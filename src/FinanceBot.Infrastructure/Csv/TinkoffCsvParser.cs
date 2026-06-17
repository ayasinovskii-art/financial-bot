using System.Globalization;
using FinanceBot.Application.Csv;

namespace FinanceBot.Infrastructure.Csv;

/// <summary>
/// Парсит CSV-выписку Тинькофф (разделитель — точка с запятой).
/// Ключевые колонки: «Дата операции», «Сумма платежа», «Валюта платежа», «Статус», «Описание».
/// Оставляет только строки Status=OK, Currency=RUB, Amount &lt; 0.
/// Amount конвертируется в положительный decimal. Дедупликация внутри батча по (Amount, Date, Description).
/// </summary>
public sealed class TinkoffCsvParser : ICsvImportParser
{
    private const string ColDate = "Дата операции";
    private const string ColAmount = "Сумма платежа";
    private const string ColCurrency = "Валюта платежа";
    private const string ColStatus = "Статус";
    private const string ColDescription = "Описание";

    public CsvParseResult Parse(string csvText)
    {
        if (string.IsNullOrWhiteSpace(csvText))
            return new CsvParseResult([], 0);

        var lines = csvText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return new CsvParseResult([], 0);

        // Find header line (first non-empty line)
        var header = lines[0].Split(';');
        var idx = BuildIndex(header);

        if (!idx.TryGetValue(ColDate, out var dateCol) ||
            !idx.TryGetValue(ColAmount, out var amountCol) ||
            !idx.TryGetValue(ColCurrency, out var currencyCol) ||
            !idx.TryGetValue(ColStatus, out var statusCol) ||
            !idx.TryGetValue(ColDescription, out var descCol))
        {
            // Required columns missing — not a Tinkoff CSV
            return new CsvParseResult([], 0);
        }

        var rows = new List<ParsedImportRow>();
        var dedupKeys = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;

        for (var i = 1; i < lines.Length; i++)
        {
            var cols = SplitLine(lines[i]);
            if (cols.Length <= Math.Max(Math.Max(dateCol, amountCol), Math.Max(currencyCol, Math.Max(statusCol, descCol))))
            {
                skipped++;
                continue;
            }

            var status = cols[statusCol].Trim().Trim('"');
            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var currency = cols[currencyCol].Trim().Trim('"');
            if (!string.Equals(currency, "RUB", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var amountStr = cols[amountCol].Trim().Trim('"').Replace(',', '.');
            if (!decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                skipped++;
                continue;
            }

            if (amount >= 0m)
            {
                // Only expenses (negative amounts)
                skipped++;
                continue;
            }

            var rawDate = cols[dateCol].Trim().Trim('"');
            if (!TryParseDate(rawDate, out var date))
            {
                skipped++;
                continue;
            }

            var description = cols[descCol].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(description))
                description = "(без описания)";

            var positiveAmount = -amount;
            var dedupKey = $"{positiveAmount}|{date:yyyy-MM-dd}|{description}";
            if (!dedupKeys.Add(dedupKey))
            {
                skipped++;
                continue;
            }

            rows.Add(new ParsedImportRow(positiveAmount, date, description));
        }

        return new CsvParseResult(rows, skipped);
    }

    private static Dictionary<string, int> BuildIndex(string[] header)
    {
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++)
        {
            var key = header[i].Trim().Trim('"');
            idx.TryAdd(key, i);
        }
        return idx;
    }

    private static string[] SplitLine(string line)
        => line.Split(';');

    private static bool TryParseDate(string raw, out DateOnly date)
    {
        // Tinkoff format: "DD.MM.YYYY HH:MM:SS" or "DD.MM.YYYY"
        ReadOnlySpan<char> datePart = raw.Length >= 10 ? raw.AsSpan(0, 10) : raw.AsSpan();
        return DateOnly.TryParseExact(datePart, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
