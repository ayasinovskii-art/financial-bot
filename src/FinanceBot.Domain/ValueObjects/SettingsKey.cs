namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Ключи per-user settings. См. раздел 8.3 ТЗ.
/// </summary>
public enum SettingsKey
{
    Timezone = 1,
    EveningTime = 2,
    SalaryDays = 3,
    ShiftRule = 4,
    SilenceDeadlineHours = 5,
    AutoConfirmRecurring = 6,
    AutoConfirmOnSilence = 7,
    PeriodType = 8,
    Allocation = 9,
    BucketMapping = 10
}

public static class SettingsKeyExtensions
{
    public static IReadOnlyCollection<SettingsKey> All { get; } =
        Enum.GetValues<SettingsKey>();

    /// <summary>Канонический строковый ключ (snake_case) для парсинга команд /settings.</summary>
    public static string ToWireName(this SettingsKey key) => key switch
    {
        SettingsKey.Timezone => "timezone",
        SettingsKey.EveningTime => "evening_time",
        SettingsKey.SalaryDays => "salary_days",
        SettingsKey.ShiftRule => "shift_rule",
        SettingsKey.SilenceDeadlineHours => "silence_deadline_hours",
        SettingsKey.AutoConfirmRecurring => "auto_confirm_recurring",
        SettingsKey.AutoConfirmOnSilence => "auto_confirm_on_silence",
        SettingsKey.PeriodType => "period_type",
        SettingsKey.Allocation => "allocation",
        SettingsKey.BucketMapping => "bucket_mapping",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown settings key.")
    };

    public static bool TryFromWireName(string? wire, out SettingsKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(wire))
        {
            return false;
        }

        switch (wire.Trim().ToLowerInvariant())
        {
            case "timezone":
                key = SettingsKey.Timezone;
                return true;
            case "evening_time":
                key = SettingsKey.EveningTime;
                return true;
            case "salary_days":
                key = SettingsKey.SalaryDays;
                return true;
            case "shift_rule":
                key = SettingsKey.ShiftRule;
                return true;
            case "silence_deadline_hours":
                key = SettingsKey.SilenceDeadlineHours;
                return true;
            case "auto_confirm_recurring":
                key = SettingsKey.AutoConfirmRecurring;
                return true;
            case "auto_confirm_on_silence":
                key = SettingsKey.AutoConfirmOnSilence;
                return true;
            case "period_type":
                key = SettingsKey.PeriodType;
                return true;
            case "allocation":
                key = SettingsKey.Allocation;
                return true;
            case "bucket_mapping":
                key = SettingsKey.BucketMapping;
                return true;
            default:
                return false;
        }
    }
}
