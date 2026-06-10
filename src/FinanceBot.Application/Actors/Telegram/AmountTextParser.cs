using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Парсер аргументов команд /income, /expense, /expense_day, а также свободного текста.
/// Поддерживает форматы:
///   "&lt;amount&gt;"
///   "&lt;date&gt; &lt;amount&gt; &lt;description?&gt;"      (date в формате yyyy-MM-dd)
///   "&lt;amount&gt; &lt;description&gt;"
///   "&lt;description&gt; &lt;amount&gt;"
/// Сумма понимает короткие форматы: суффикс валюты («500р», «500 ₽», «500руб»),
/// множитель тысяч («1.5к», «2k» — только приклеенный, т.к. «к» — частый предлог),
/// пробел/неразрывный пробел как разделитель тысяч («2 000», «2 000,50»).
/// Десятичный разделитель — «.» или «,».
/// </summary>
public static partial class AmountTextParser
{
    // Число: либо группы тысяч через пробел/NBSP/узкий NBSP («2 000», «12 345 678,50»),
    // либо обычное число с опциональной дробной частью («750», «1.5», «750,50»).
    private const string NumberPattern =
        @"\d{1,3}(?:[   ]\d{3})+(?:[.,]\d+)?|\d+(?:[.,]\d+)?";

    // Суффикс: множитель «к/k» — только вплотную к числу; валюта — можно через пробел.
    // «\.?» съедает точку после «руб.»/«р.».
    private const string SuffixPattern =
        @"(?:(?<mult>[кk])|\s*(?<cur>руб|р|rub|₽))?\.?";

    [GeneratedRegex($@"^(?<num>{NumberPattern}){SuffixPattern}(?=\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AmountAtStart();

    [GeneratedRegex($@"(?<=^|\s)(?<num>{NumberPattern}){SuffixPattern}$", RegexOptions.IgnoreCase)]
    private static partial Regex AmountAtEnd();

    public static AmountTextParseResult? TryParseSingle(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        DateOnly? date = null;
        var body = trimmed;

        var firstToken = trimmed.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (firstToken.Length > 0 &&
            DateOnly.TryParseExact(firstToken[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            date = parsedDate;
            body = firstToken.Length == 2 ? firstToken[1] : string.Empty;
            if (body.Length == 0)
            {
                return null;
            }
        }

        // Сумма в начале: "<amount> <desc?>"
        var start = AmountAtStart().Match(body);
        if (start.Success && TryParseMatch(start, out var amount))
        {
            var description = body[start.Length..].Trim();
            return new AmountTextParseResult(date, amount, description.Length == 0 ? null : description);
        }

        // Сумма в конце: "<desc> <amount>" («обед 750», «такси 1.5к»)
        var end = AmountAtEnd().Match(body);
        if (end.Success && TryParseMatch(end, out amount))
        {
            var description = body[..end.Index].Trim();
            return new AmountTextParseResult(date, amount, description.Length == 0 ? null : description);
        }

        return null;
    }

    public static IReadOnlyList<AmountTextParseResult> ParseMultiple(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        var result = new List<AmountTextParseResult>();
        foreach (var seg in SplitSegments(trimmed))
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
        amount = 0m;
        var trimmed = token.Trim();
        var match = AmountAtStart().Match(trimmed);
        // Токен должен состоять из суммы целиком — без хвостового мусора.
        return match.Success && match.Length == trimmed.Length && TryParseMatch(match, out amount);
    }

    /// <summary>
    /// Разделители сегментов — '+' и ','; запятая между двумя цифрами — десятичный
    /// разделитель («2 000,50»), а не граница сегментов.
    /// </summary>
    private static IEnumerable<string> SplitSegments(string text)
    {
        var startIndex = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var isDecimalComma = c == ',' &&
                                 i > 0 && char.IsAsciiDigit(text[i - 1]) &&
                                 i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]);
            if (c == '+' || (c == ',' && !isDecimalComma))
            {
                var segment = text[startIndex..i].Trim();
                if (segment.Length > 0)
                {
                    yield return segment;
                }
                startIndex = i + 1;
            }
        }

        var tail = text[startIndex..].Trim();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    private static bool TryParseMatch(Match match, out decimal amount)
    {
        var digits = match.Groups["num"].Value
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(',', '.');
        if (!decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }

        if (match.Groups["mult"].Success)
        {
            amount *= 1000m;
        }
        return amount > 0m;
    }
}

/// <summary>Результат парсинга одного фрагмента команды/текста.</summary>
public sealed record AmountTextParseResult(DateOnly? Date, decimal Amount, string? Description);
