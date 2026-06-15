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
    BucketMapping = 10,
    SalaryAmount = 11,
    NotificationsEnabled = 12
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
        SettingsKey.SalaryAmount => "salary_amount",
        SettingsKey.NotificationsEnabled => "notifications_enabled",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown settings key.")
    };

    /// <summary>Канонический строковый default per ТЗ §8.3 для отображения в /settings.</summary>
    public static string DefaultWireValue(this SettingsKey key) => key switch
    {
        SettingsKey.Timezone => "UTC",
        SettingsKey.EveningTime => "19:00",
        SettingsKey.SalaryDays => "10,25",
        SettingsKey.ShiftRule => "previous",
        SettingsKey.SilenceDeadlineHours => "4",
        SettingsKey.AutoConfirmRecurring => "false",
        SettingsKey.AutoConfirmOnSilence => "false",
        SettingsKey.PeriodType => "salary-cycle",
        SettingsKey.Allocation => "50/25/25",
        SettingsKey.BucketMapping => "(пусто)",
        SettingsKey.SalaryAmount => "(не задано)",
        SettingsKey.NotificationsEnabled => "false",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown settings key.")
    };

    /// <summary>Краткое описание ключа для отображения в /settings.</summary>
    public static string Description(this SettingsKey key) => key switch
    {
        SettingsKey.Timezone => "IANA-таймзона пользователя; влияет на расписание тиков.",
        SettingsKey.EveningTime => "Время вечернего тика напоминаний (HH:mm в таймзоне пользователя).",
        SettingsKey.SalaryDays => "Дни месяца, в которые приходит зарплата/аванс (1..28).",
        SettingsKey.ShiftRule => "Сдвиг тика, если salary_day выпал на выходной: previous|next|none.",
        SettingsKey.SilenceDeadlineHours => "Сколько часов после вечернего тика ждать ответа (1..24).",
        SettingsKey.AutoConfirmRecurring => "Авто-подтверждение повторяющихся плановых трат (true|false).",
        SettingsKey.AutoConfirmOnSilence => "Авто-подтверждение, если пользователь промолчал в дедлайн (true|false).",
        SettingsKey.PeriodType => "Тип бюджетного периода: salary-cycle | calendar-month.",
        SettingsKey.Allocation => "Доли бюджета essentials/fun/deposit в процентах (сумма = 100).",
        SettingsKey.BucketMapping => "Переопределение маппинга категория → бакет (поверх дефолта).",
        SettingsKey.SalaryAmount => "Ожидаемая сумма зарплаты/аванса, параллельно salary_days. Используется advisor'ом для прогнозов.",
        SettingsKey.NotificationsEnabled => "Проактивные push-сообщения от бота (true|false). По умолчанию выключено.",
        _ => string.Empty
    };

    /// <summary>Пример значения для подсказки в /settings.</summary>
    public static string Example(this SettingsKey key) => key switch
    {
        SettingsKey.Timezone => "Europe/Moscow",
        SettingsKey.EveningTime => "19:30",
        SettingsKey.SalaryDays => "10,25",
        SettingsKey.ShiftRule => "previous",
        SettingsKey.SilenceDeadlineHours => "4",
        SettingsKey.AutoConfirmRecurring => "true",
        SettingsKey.AutoConfirmOnSilence => "false",
        SettingsKey.PeriodType => "salary-cycle",
        SettingsKey.Allocation => "50/25/25",
        SettingsKey.BucketMapping => "DiningOut=Essentials,Subscriptions=Fun",
        SettingsKey.SalaryAmount => "30000,80000",
        SettingsKey.NotificationsEnabled => "true",
        _ => string.Empty
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
            case "salary_amount":
                key = SettingsKey.SalaryAmount;
                return true;
            case "notifications_enabled":
                key = SettingsKey.NotificationsEnabled;
                return true;
            default:
                return false;
        }
    }
}
