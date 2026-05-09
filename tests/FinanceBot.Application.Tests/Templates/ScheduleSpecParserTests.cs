using FinanceBot.Application.Actors.UserTemplates;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Templates;

public sealed class ScheduleSpecParserTests
{
    [Fact]
    public void Weekdays_parses_to_singleton()
    {
        ScheduleSpecParser.TryParse("weekdays", out var spec, out _).Should().BeTrue();
        spec.Should().BeOfType<WeekdaysSchedule>();
    }

    [Fact]
    public void Daily_parses_to_singleton()
    {
        ScheduleSpecParser.TryParse("daily", out var spec, out _).Should().BeTrue();
        spec.Should().BeOfType<DailySchedule>();
    }

    [Fact]
    public void DaysOfWeek_parses_with_sorting_and_dedup()
    {
        ScheduleSpecParser.TryParse("dow:5,1,3,1", out var spec, out _).Should().BeTrue();
        var dow = spec.Should().BeOfType<DaysOfWeekSchedule>().Subject;
        dow.Days.Should().BeEquivalentTo([1, 3, 5]);
    }

    [Fact]
    public void DaysOfMonth_validates_28_max()
    {
        ScheduleSpecParser.TryParse("dom:28", out var ok, out _).Should().BeTrue();
        ok.Should().BeOfType<DaysOfMonthSchedule>();

        ScheduleSpecParser.TryParse("dom:29", out _, out var error).Should().BeFalse();
        error.Should().Contain("1..28");
    }

    [Theory]
    [InlineData("dow:0")]
    [InlineData("dow:8")]
    [InlineData("dow:")]
    [InlineData("dow:abc")]
    [InlineData("nonsense")]
    [InlineData("")]
    public void Invalid_inputs_rejected(string raw)
    {
        ScheduleSpecParser.TryParse(raw, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Format_roundtrips_dow()
    {
        ScheduleSpecParser.TryParse("dow:1,3,5", out var spec, out _).Should().BeTrue();
        spec!.Format().Should().Be("dow:1,3,5");
    }
}
