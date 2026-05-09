using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;

namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Расписание регулярного шаблона. Sealed hierarchy для exhaustive pattern matching.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(WeekdaysSchedule), "weekdays")]
[JsonDerivedType(typeof(DailySchedule), "daily")]
[JsonDerivedType(typeof(DaysOfWeekSchedule), "dow")]
[JsonDerivedType(typeof(DaysOfMonthSchedule), "dom")]
public abstract record ScheduleSpec
{
    private protected ScheduleSpec() { }

    /// <summary>Сериализованная форма для отображения в /template list.</summary>
    public abstract string Format();
}

/// <summary>Каждый рабочий день (Mon–Fri за вычетом праздников по IWorkdayCalendar).</summary>
public sealed record WeekdaysSchedule : ScheduleSpec
{
    public static WeekdaysSchedule Instance { get; } = new();
    public override string Format() => "weekdays";
}

/// <summary>Каждый день без исключений.</summary>
public sealed record DailySchedule : ScheduleSpec
{
    public static DailySchedule Instance { get; } = new();
    public override string Format() => "daily";
}

/// <summary>По дням недели (1=Mon, 7=Sun, ISO 8601).</summary>
public sealed record DaysOfWeekSchedule(ImmutableArray<int> Days) : ScheduleSpec
{
    public override string Format()
        => "dow:" + string.Join(",", Days.Select(d => d.ToString(CultureInfo.InvariantCulture)));

    public bool ContainsIsoDay(int isoDay) => Days.Contains(isoDay);
}

/// <summary>По числам месяца (1..28).</summary>
public sealed record DaysOfMonthSchedule(ImmutableArray<int> Days) : ScheduleSpec
{
    public override string Format()
        => "dom:" + string.Join(",", Days.Select(d => d.ToString(CultureInfo.InvariantCulture)));

    public bool ContainsDay(int dayOfMonth) => Days.Contains(dayOfMonth);
}
