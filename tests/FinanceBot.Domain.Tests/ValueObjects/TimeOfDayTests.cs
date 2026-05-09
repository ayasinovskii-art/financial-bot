using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Domain.Tests.ValueObjects;

public sealed class TimeOfDayTests
{
    [Theory]
    [InlineData("00:00", 0, 0)]
    [InlineData("9:30", 9, 30)]
    [InlineData("19:00", 19, 0)]
    [InlineData("23:59", 23, 59)]
    public void TryParse_valid(string input, int hour, int minute)
    {
        TimeOfDay.TryParse(input, out var t).Should().BeTrue();
        t.Hour.Should().Be(hour);
        t.Minute.Should().Be(minute);
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("abc")]
    [InlineData("19")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_invalid_returns_false(string? input)
    {
        TimeOfDay.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void ToString_uses_HHmm_format()
    {
        new TimeOfDay(9, 5).ToString().Should().Be("09:05");
        new TimeOfDay(19, 0).ToString().Should().Be("19:00");
    }

    [Fact]
    public void Constructor_rejects_out_of_range()
    {
        FluentActions.Invoking(() => new TimeOfDay(24, 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new TimeOfDay(12, 60)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
