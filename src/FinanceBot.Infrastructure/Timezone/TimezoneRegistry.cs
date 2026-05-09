using FinanceBot.Domain.Services;

namespace FinanceBot.Infrastructure.Timezone;

public sealed class TimezoneRegistry : ITimezoneRegistry
{
    public TimeZoneInfo Get(string ianaName) => TimeZoneInfo.FindSystemTimeZoneById(ianaName);

    public bool TryGet(string ianaName, out TimeZoneInfo? timezone)
    {
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(ianaName);
            return true;
        }
        catch (Exception)
        {
            timezone = null;
            return false;
        }
    }

    public TimeZoneInfo Default { get; } = TimeZoneInfo.Utc;

    public bool IsValid(string ianaName) => TryGet(ianaName, out _);
}
