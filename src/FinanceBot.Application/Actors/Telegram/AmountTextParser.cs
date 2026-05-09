using System.Globalization;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Парсер аргументов команд /income, /expense, /expense_day, а также свободного текста.
/// Поддерживает форматы:
///   "&lt;amount&gt;"
///   "&lt;date&gt; &lt;amount&gt; &lt;description?&gt;"      (date в формате yyyy-MM-dd)
///   "&lt;amount&gt; &lt;description&gt;"
/// </summary>
public static class AmountTextParser
{
    public static AmountTextParseResult? TryParseSingle(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var tokens = trimmed.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        DateOnly? date = null;
        string head = tokens[0];
        string rest = tokens.Length == 2 ? tokens[1] : string.Empty;

        if (DateOnly.TryParseExact(head, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            date = parsedDate;
            // amount must follow
            var afterDate = rest.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (afterDate.Length == 0)
            {
                return null;
            }
            head = afterDate[0];
            rest = afterDate.Length == 2 ? afterDate[1] : string.Empty;
        }

        if (!TryParseAmount(head, out var amount))
        {
            return null;
        }

        var description = rest.Trim();
        return new AmountTextParseResult(date, amount, description.Length == 0 ? null : description);
    }

    public static IReadOnlyList<AmountTextParseResult> ParseMultiple(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        // Разделители — '+' и ',' между сегментами вида "<amount> <desc>" / "<date> <amount> <desc>".
        var segments = trimmed.Split(['+', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<AmountTextParseResult>(segments.Length);
        foreach (var seg in segments)
        {
            var parsed = TryParseSingle(seg);
            if (parsed is null)
            {
                return [];
            }
            result.Add(parsed);
        }
        return result;
    }

    public static bool TryParseAmount(string token, out decimal amount)
    {
        var normalized = token.Replace(',', '.');
        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out amount) && amount > 0m;
    }
}

/// <summary>Результат парсинга одного фрагмента команды/текста.</summary>
public sealed record AmountTextParseResult(DateOnly? Date, decimal Amount, string? Description);
