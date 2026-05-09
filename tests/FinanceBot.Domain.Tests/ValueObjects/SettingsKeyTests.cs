using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Domain.Tests.ValueObjects;

public sealed class SettingsKeyTests
{
    [Theory]
    [InlineData(SettingsKey.Timezone, "timezone")]
    [InlineData(SettingsKey.EveningTime, "evening_time")]
    [InlineData(SettingsKey.SalaryDays, "salary_days")]
    [InlineData(SettingsKey.ShiftRule, "shift_rule")]
    [InlineData(SettingsKey.SilenceDeadlineHours, "silence_deadline_hours")]
    [InlineData(SettingsKey.AutoConfirmRecurring, "auto_confirm_recurring")]
    [InlineData(SettingsKey.AutoConfirmOnSilence, "auto_confirm_on_silence")]
    [InlineData(SettingsKey.PeriodType, "period_type")]
    [InlineData(SettingsKey.Allocation, "allocation")]
    [InlineData(SettingsKey.BucketMapping, "bucket_mapping")]
    public void ToWireName_roundtrips(SettingsKey key, string wire)
    {
        key.ToWireName().Should().Be(wire);
        SettingsKeyExtensions.TryFromWireName(wire, out var parsed).Should().BeTrue();
        parsed.Should().Be(key);
    }

    [Theory]
    [InlineData("TIMEZONE")]
    [InlineData("Timezone")]
    [InlineData("  timezone  ")]
    public void TryFromWireName_is_case_insensitive_and_trims(string raw)
    {
        SettingsKeyExtensions.TryFromWireName(raw, out var key).Should().BeTrue();
        key.Should().Be(SettingsKey.Timezone);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonexistent")]
    public void TryFromWireName_returns_false_for_unknown(string? raw)
    {
        SettingsKeyExtensions.TryFromWireName(raw, out _).Should().BeFalse();
    }

    [Fact]
    public void All_returns_every_value()
    {
        SettingsKeyExtensions.All.Should().HaveCount(Enum.GetValues<SettingsKey>().Length);
    }
}
