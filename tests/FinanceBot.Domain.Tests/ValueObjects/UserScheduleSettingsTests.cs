using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Domain.Tests.ValueObjects;

public sealed class UserScheduleSettingsTests
{
    [Fact]
    public void Default_values_match_spec_3_11()
    {
        var s = UserScheduleSettings.Default;
        s.EveningTime.Should().Be(TimeOfDay.Evening);
        s.SalaryDays.Should().Equal(10, 25);
        s.ShiftRule.Should().Be(ShiftRule.Previous);
        s.SilenceDeadlineHours.Should().Be(4);
        s.AutoConfirmRecurring.Should().BeFalse();
        s.AutoConfirmOnSilence.Should().BeFalse();
    }

    [Fact]
    public void FromDictionary_parses_known_keys()
    {
        var dict = new Dictionary<string, string?>
        {
            [SettingsKey.EveningTime.ToWireName()] = "20:30",
            [SettingsKey.SalaryDays.ToWireName()] = "5,15,25",
            [SettingsKey.ShiftRule.ToWireName()] = "next",
            [SettingsKey.SilenceDeadlineHours.ToWireName()] = "6",
            [SettingsKey.AutoConfirmOnSilence.ToWireName()] = "true",
            [SettingsKey.AutoConfirmRecurring.ToWireName()] = "true"
        };

        var s = UserScheduleSettings.FromDictionary(dict, TimeZoneInfo.Utc);
        s.EveningTime.Should().Be(new TimeOfDay(20, 30));
        s.SalaryDays.Should().Equal(5, 15, 25);
        s.ShiftRule.Should().Be(ShiftRule.Next);
        s.SilenceDeadlineHours.Should().Be(6);
        s.AutoConfirmOnSilence.Should().BeTrue();
        s.AutoConfirmRecurring.Should().BeTrue();
    }

    [Fact]
    public void FromDictionary_falls_back_on_invalid_values()
    {
        var dict = new Dictionary<string, string?>
        {
            [SettingsKey.EveningTime.ToWireName()] = "boom",
            [SettingsKey.SalaryDays.ToWireName()] = "99,bad,5",
            [SettingsKey.SilenceDeadlineHours.ToWireName()] = "100"
        };

        var s = UserScheduleSettings.FromDictionary(dict, TimeZoneInfo.Utc);
        s.EveningTime.Should().Be(TimeOfDay.Evening);
        s.SalaryDays.Should().Equal(5);
        s.SilenceDeadlineHours.Should().Be(4);
    }

    [Fact]
    public void FromDictionary_uses_provided_default_timezone_if_unset()
    {
        var dict = new Dictionary<string, string?>();
        var s = UserScheduleSettings.FromDictionary(dict, TimeZoneInfo.FindSystemTimeZoneById("UTC"));
        s.Timezone.Id.Should().Be("UTC");
    }
}
