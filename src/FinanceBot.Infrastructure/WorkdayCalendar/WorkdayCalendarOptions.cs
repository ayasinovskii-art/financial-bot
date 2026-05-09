namespace FinanceBot.Infrastructure.WorkdayCalendar;

public sealed class WorkdayCalendarOptions
{
    public const string SectionName = "WorkdayCalendar";

    public string Provider { get; init; } = "isdayoff"; // isdayoff | static
    public string BaseUrl { get; init; } = "https://isdayoff.ru";
    public string CountryCode { get; init; } = "ru";
}
