using System.Globalization;

namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Время суток HH:mm (без секунд и таймзоны). Используется для evening_time, salary_day_time и т.п.
/// </summary>
public readonly record struct TimeOfDay
{
    public int Hour { get; }
    public int Minute { get; }

    public TimeOfDay(int hour, int minute)
    {
        if (hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), hour, "Hour must be 0..23.");
        }
        if (minute is < 0 or > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), minute, "Minute must be 0..59.");
        }
        Hour = hour;
        Minute = minute;
    }

    public static readonly TimeOfDay Evening = new(19, 0);
    public static readonly TimeOfDay Morning = new(9, 0);

    public TimeOnly ToTimeOnly() => new(Hour, Minute);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Hour:D2}:{Minute:D2}");

    public static bool TryParse(string? value, out TimeOfDay result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute))
        {
            return false;
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return false;
        }

        result = new TimeOfDay(hour, minute);
        return true;
    }

    public static TimeOfDay Parse(string value)
    {
        if (!TryParse(value, out var result))
        {
            throw new FormatException($"Invalid time-of-day '{value}'. Expected HH:mm.");
        }
        return result;
    }
}
