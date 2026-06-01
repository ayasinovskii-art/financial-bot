using System.Globalization;
using System.Linq;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Settings;

/// <summary>
/// Валидация и нормализация значений per-user settings (см. ТЗ §8.3).
/// Парсит человекочитаемое значение из команды и нормализует в каноническую форму, которую UsersListProjection
/// сохраняет в <c>app.users.settings_json</c>.
/// </summary>
public static class SettingsValueValidator
{
    public static bool TryValidate(SettingsKey key, string rawValue, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0)
        {
            error = "Значение не указано.";
            return false;
        }

        return key switch
        {
            SettingsKey.Timezone => ValidateTimezone(trimmed, out normalized, out error),
            SettingsKey.EveningTime => ValidateTimeOfDay(trimmed, out normalized, out error),
            SettingsKey.SalaryDays => ValidateSalaryDays(trimmed, out normalized, out error),
            SettingsKey.ShiftRule => ValidateShiftRule(trimmed, out normalized, out error),
            SettingsKey.SilenceDeadlineHours => ValidateInt(trimmed, 1, 24, out normalized, out error),
            SettingsKey.AutoConfirmRecurring => ValidateBool(trimmed, out normalized, out error),
            SettingsKey.AutoConfirmOnSilence => ValidateBool(trimmed, out normalized, out error),
            SettingsKey.PeriodType => ValidatePeriodType(trimmed, out normalized, out error),
            SettingsKey.Allocation => ValidateAllocation(trimmed, out normalized, out error),
            SettingsKey.BucketMapping => ValidateBucketMapping(trimmed, out normalized, out error),
            SettingsKey.SalaryAmount => ValidateSalaryAmount(trimmed, out normalized, out error),
            _ => Fail("Неизвестный ключ настройки.", out normalized, out error)
        };
    }

    private static bool ValidateSalaryAmount(string raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Укажи хотя бы одну сумму, например `80000` или `30000,80000`.";
            return false;
        }

        var amounts = new List<decimal>(parts.Length);
        foreach (var p in parts)
        {
            if (!decimal.TryParse(p, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0m)
            {
                error = $"Сумма '{p}' должна быть положительным числом.";
                return false;
            }
            amounts.Add(amount);
        }

        normalized = string.Join(",", amounts.Select(a => a.ToString("0.##", CultureInfo.InvariantCulture)));
        return true;
    }

    private static bool ValidateTimezone(string raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(raw);
            normalized = tz.Id;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            error = $"Неизвестная таймзона '{raw}'. Используй IANA-формат, например `Europe/Moscow`.";
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            error = $"Невалидная таймзона '{raw}'.";
            return false;
        }
    }

    private static bool ValidateTimeOfDay(string raw, out string normalized, out string error)
    {
        if (TimeOfDay.TryParse(raw, out var t))
        {
            normalized = t.ToString();
            error = string.Empty;
            return true;
        }
        normalized = string.Empty;
        error = "Ожидается формат `HH:mm`, например `19:00`.";
        return false;
    }

    private static bool ValidateSalaryDays(string raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Укажи хотя бы один день, например `10,25`.";
            return false;
        }

        var days = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (!int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) || d is < 1 or > 28)
            {
                error = $"День '{p}' вне диапазона 1..28.";
                return false;
            }
            days.Add(d);
        }

        normalized = string.Join(",", days.Distinct().OrderBy(d => d));
        return true;
    }

    private static bool ValidateShiftRule(string raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        switch (raw.ToLowerInvariant())
        {
            case "previous": normalized = "previous"; return true;
            case "next": normalized = "next"; return true;
            case "none": normalized = "none"; return true;
            default:
                error = "Допустимые значения: `previous`, `next`, `none`.";
                return false;
        }
    }

    private static bool ValidateInt(string raw, int min, int max, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
        {
            error = $"Целое число от {min} до {max}.";
            return false;
        }
        normalized = n.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool ValidateBool(string raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        switch (raw.ToLowerInvariant())
        {
            case "true": case "1": case "yes": case "on": case "да":
                normalized = "true"; return true;
            case "false": case "0": case "no": case "off": case "нет":
                normalized = "false"; return true;
            default:
                error = "Допустимые значения: `true` / `false`.";
                return false;
        }
    }

    private static bool ValidatePeriodType(string raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        switch (raw.ToLowerInvariant())
        {
            case "salary-cycle": normalized = "salary-cycle"; return true;
            case "calendar-month": normalized = "calendar-month"; return true;
            default:
                error = "Допустимые значения: `salary-cycle`, `calendar-month`.";
                return false;
        }
    }

    private static bool ValidateAllocation(string raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            error = "Формат `essentials/fun/deposit`, например `50/25/25`.";
            return false;
        }
        var nums = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out nums[i]) || nums[i] is < 0 or > 100)
            {
                error = $"Часть '{parts[i]}' должна быть числом 0..100.";
                return false;
            }
        }

        try
        {
            var ratio = new AllocationRatios(nums[0], nums[1], nums[2]);
            normalized = ratio.ToString();
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool ValidateBucketMapping(string raw, out string normalized, out string error)
    {
        // Формат: "Groceries=Essentials,DiningOut=Fun,..."
        normalized = string.Empty;
        error = string.Empty;

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Формат `Category=Bucket,...`, например `DiningOut=Essentials`.";
            return false;
        }

        var pairs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in parts)
        {
            var kv = p.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2)
            {
                error = $"Не разобрал '{p}'. Формат `Category=Bucket`.";
                return false;
            }
            if (!CategoryExtensions.TryParse(kv[0], out var cat))
            {
                error = $"Неизвестная категория '{kv[0]}'.";
                return false;
            }
            if (!Enum.TryParse<Bucket>(kv[1], ignoreCase: true, out var bucket) || !Enum.IsDefined(bucket))
            {
                error = $"Неизвестный бакет '{kv[1]}'.";
                return false;
            }
            pairs[cat.ToString()] = bucket.ToString();
        }

        normalized = string.Join(",", pairs.Select(p => $"{p.Key}={p.Value}"));
        return true;
    }

    private static bool Fail(string message, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = message;
        return false;
    }
}
