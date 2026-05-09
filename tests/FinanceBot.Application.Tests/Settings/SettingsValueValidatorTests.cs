using FinanceBot.Application.Settings;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Settings;

public sealed class SettingsValueValidatorTests
{
    [Theory]
    [InlineData("UTC", "UTC")]
    public void Timezone_accepts_known(string raw, string normalized)
    {
        SettingsValueValidator.TryValidate(SettingsKey.Timezone, raw, out var n, out _).Should().BeTrue();
        n.Should().Be(normalized);
    }

    [Fact]
    public void Timezone_rejects_unknown()
    {
        SettingsValueValidator.TryValidate(SettingsKey.Timezone, "Mars/Olympus", out _, out var error).Should().BeFalse();
        error.Should().Contain("таймзона");
    }

    [Theory]
    [InlineData("19:00", "19:00")]
    [InlineData("9:30", "09:30")]
    public void EveningTime_accepts_HH_mm(string raw, string normalized)
    {
        SettingsValueValidator.TryValidate(SettingsKey.EveningTime, raw, out var n, out _).Should().BeTrue();
        n.Should().Be(normalized);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("garbage")]
    public void EveningTime_rejects_invalid(string raw)
    {
        SettingsValueValidator.TryValidate(SettingsKey.EveningTime, raw, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("10,25", "10,25")]
    [InlineData("25,10", "10,25")]
    [InlineData("5", "5")]
    [InlineData("28", "28")]
    public void SalaryDays_accepts_and_normalises(string raw, string normalized)
    {
        SettingsValueValidator.TryValidate(SettingsKey.SalaryDays, raw, out var n, out _).Should().BeTrue();
        n.Should().Be(normalized);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("29")]
    [InlineData("abc")]
    [InlineData("")]
    public void SalaryDays_rejects_invalid(string raw)
    {
        SettingsValueValidator.TryValidate(SettingsKey.SalaryDays, raw, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("previous", "previous")]
    [InlineData("PREVIOUS", "previous")]
    [InlineData("next", "next")]
    [InlineData("none", "none")]
    public void ShiftRule_accepts(string raw, string normalized)
    {
        SettingsValueValidator.TryValidate(SettingsKey.ShiftRule, raw, out var n, out _).Should().BeTrue();
        n.Should().Be(normalized);
    }

    [Fact]
    public void ShiftRule_rejects_unknown()
    {
        SettingsValueValidator.TryValidate(SettingsKey.ShiftRule, "later", out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("4", "4")]
    [InlineData("1", "1")]
    [InlineData("24", "24")]
    public void SilenceDeadlineHours_accepts(string raw, string normalized)
    {
        SettingsValueValidator.TryValidate(SettingsKey.SilenceDeadlineHours, raw, out var n, out _).Should().BeTrue();
        n.Should().Be(normalized);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("25")]
    public void SilenceDeadlineHours_rejects_out_of_range(string raw)
    {
        SettingsValueValidator.TryValidate(SettingsKey.SilenceDeadlineHours, raw, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("yes", "true")]
    [InlineData("no", "false")]
    [InlineData("да", "true")]
    [InlineData("нет", "false")]
    [InlineData("1", "true")]
    [InlineData("0", "false")]
    public void Bool_settings_accept_synonyms(string raw, string normalized)
    {
        SettingsValueValidator.TryValidate(SettingsKey.AutoConfirmRecurring, raw, out var n, out _).Should().BeTrue();
        n.Should().Be(normalized);
    }

    [Theory]
    [InlineData("salary-cycle", "salary-cycle")]
    [InlineData("calendar-month", "calendar-month")]
    public void PeriodType_accepts_two_values(string raw, string normalized)
    {
        SettingsValueValidator.TryValidate(SettingsKey.PeriodType, raw, out var n, out _).Should().BeTrue();
        n.Should().Be(normalized);
    }

    [Theory]
    [InlineData("50/25/25", "50/25/25")]
    [InlineData("60/30/10", "60/30/10")]
    public void Allocation_accepts_valid_sum_to_100(string raw, string normalized)
    {
        SettingsValueValidator.TryValidate(SettingsKey.Allocation, raw, out var n, out _).Should().BeTrue();
        n.Should().Be(normalized);
    }

    [Theory]
    [InlineData("60/30/20")]
    [InlineData("100/0")]
    [InlineData("garbage")]
    public void Allocation_rejects_invalid(string raw)
    {
        SettingsValueValidator.TryValidate(SettingsKey.Allocation, raw, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void BucketMapping_accepts_valid_pairs()
    {
        SettingsValueValidator.TryValidate(
            SettingsKey.BucketMapping, "DiningOut=Essentials,Travel=Essentials", out var n, out _).Should().BeTrue();
        n.Should().Contain("DiningOut=Essentials");
        n.Should().Contain("Travel=Essentials");
    }

    [Theory]
    [InlineData("Unknown=Essentials")]
    [InlineData("DiningOut=NotABucket")]
    [InlineData("malformed")]
    public void BucketMapping_rejects_invalid(string raw)
    {
        SettingsValueValidator.TryValidate(SettingsKey.BucketMapping, raw, out _, out _).Should().BeFalse();
    }
}
