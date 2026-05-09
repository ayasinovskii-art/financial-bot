using System.Collections.Immutable;
using System.Globalization;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.UserTemplates;

/// <summary>
/// Парсер строкового представления расписания: weekdays / daily / dow:1,3,5 / dom:1,15.
/// </summary>
public static class ScheduleSpecParser
{
    public static bool TryParse(string raw, out ScheduleSpec? spec, out string error)
    {
        spec = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Пустое расписание.";
            return false;
        }

        var lower = raw.Trim().ToLowerInvariant();

        if (lower == "weekdays")
        {
            spec = WeekdaysSchedule.Instance;
            return true;
        }
        if (lower == "daily")
        {
            spec = DailySchedule.Instance;
            return true;
        }

        if (lower.StartsWith("dow:", StringComparison.Ordinal))
        {
            return TryParseInts(lower[4..], 1, 7, "день недели", out spec, out error,
                arr => new DaysOfWeekSchedule(arr));
        }

        if (lower.StartsWith("dom:", StringComparison.Ordinal))
        {
            return TryParseInts(lower[4..], 1, 28, "число месяца", out spec, out error,
                arr => new DaysOfMonthSchedule(arr));
        }

        error = "Допустимые форматы: `weekdays`, `daily`, `dow:1,3,5`, `dom:1,15`.";
        return false;
    }

    private static bool TryParseInts(
        string list,
        int min,
        int max,
        string label,
        out ScheduleSpec? spec,
        out string error,
        Func<ImmutableArray<int>, ScheduleSpec> factory)
    {
        spec = null;
        error = string.Empty;

        var parts = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = $"Не указаны значения для {label}.";
            return false;
        }

        var values = new SortedSet<int>();
        foreach (var p in parts)
        {
            if (!int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < min || v > max)
            {
                error = $"Значение '{p}' вне диапазона {min}..{max} для {label}.";
                return false;
            }
            values.Add(v);
        }

        spec = factory([.. values]);
        return true;
    }
}
