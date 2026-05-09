using System.Globalization;

namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Распарсенный срез settings, нужный для расписания тиков (см. §3.11 ТЗ + §8.3).
/// Содержит дефолты, если ключ отсутствует.
/// </summary>
public sealed record UserScheduleSettings(
    TimeZoneInfo Timezone,
    TimeOfDay EveningTime,
    IReadOnlyList<int> SalaryDays,
    ShiftRule ShiftRule,
    int SilenceDeadlineHours,
    bool AutoConfirmRecurring,
    bool AutoConfirmOnSilence)
{
    public static UserScheduleSettings Default { get; } = new(
        TimeZoneInfo.Utc,
        TimeOfDay.Evening,
        [10, 25],
        ShiftRule.Previous,
        SilenceDeadlineHours: 4,
        AutoConfirmRecurring: false,
        AutoConfirmOnSilence: false);

    /// <summary>
    /// Сборка из словаря (settings_json), который хранится в UserActor.State.Settings.
    /// Невалидные значения откатываются к дефолтам без выброса.
    /// </summary>
    public static UserScheduleSettings FromDictionary(
        IReadOnlyDictionary<string, string?> settings,
        TimeZoneInfo defaultTimezone)
    {
        var tz = defaultTimezone;
        if (settings.TryGetValue(SettingsKey.Timezone.ToWireName(), out var tzRaw) && !string.IsNullOrWhiteSpace(tzRaw))
        {
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(tzRaw); }
            catch (TimeZoneNotFoundException) { /* ignore, keep default */ }
            catch (InvalidTimeZoneException) { /* ignore */ }
        }

        var evening = TimeOfDay.Evening;
        if (settings.TryGetValue(SettingsKey.EveningTime.ToWireName(), out var et)
            && TimeOfDay.TryParse(et, out var parsedEvening))
        {
            evening = parsedEvening;
        }

        var salaryDays = new[] { 10, 25 };
        if (settings.TryGetValue(SettingsKey.SalaryDays.ToWireName(), out var sdRaw) && !string.IsNullOrWhiteSpace(sdRaw))
        {
            var parsed = sdRaw!
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 28 ? n : -1)
                .Where(n => n > 0)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();
            if (parsed.Length > 0)
            {
                salaryDays = parsed;
            }
        }

        var shift = ShiftRule.Previous;
        if (settings.TryGetValue(SettingsKey.ShiftRule.ToWireName(), out var srRaw))
        {
            shift = (srRaw ?? string.Empty).ToLowerInvariant() switch
            {
                "next" => ShiftRule.Next,
                "none" => ShiftRule.None,
                "previous" => ShiftRule.Previous,
                _ => ShiftRule.Previous
            };
        }

        var silence = 4;
        if (settings.TryGetValue(SettingsKey.SilenceDeadlineHours.ToWireName(), out var shRaw)
            && int.TryParse(shRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sh)
            && sh is >= 1 and <= 24)
        {
            silence = sh;
        }

        var autoConfirmRecurring = ParseBool(settings, SettingsKey.AutoConfirmRecurring);
        var autoConfirmOnSilence = ParseBool(settings, SettingsKey.AutoConfirmOnSilence);

        return new UserScheduleSettings(tz, evening, salaryDays, shift, silence, autoConfirmRecurring, autoConfirmOnSilence);
    }

    private static bool ParseBool(IReadOnlyDictionary<string, string?> settings, SettingsKey key)
    {
        if (!settings.TryGetValue(key.ToWireName(), out var raw))
        {
            return false;
        }
        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }
}
