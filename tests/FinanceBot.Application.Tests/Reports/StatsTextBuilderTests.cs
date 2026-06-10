using FinanceBot.Application.Actors.Reports;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Reports;

public sealed class StatsTextBuilderTests
{
    private static readonly DateOnly Start = new(2026, 6, 1);

    [Fact]
    public void Empty_rows_produce_no_expenses_message()
    {
        var text = StatsTextBuilder.Build([], Start, periodsAgo: 0);

        text.Should().Contain("Трат за период нет");
    }

    [Fact]
    public void Top_categories_sorted_descending_with_share()
    {
        var rows = new[]
        {
            new CategorySpend("Groceries", 6000m),
            new CategorySpend("Transport", 1000m),
            new CategorySpend("DiningOut", 3000m),
        };

        var text = StatsTextBuilder.Build(rows, Start, periodsAgo: 0);

        text.Should().Contain("Groceries — 6000.00 ₽ (60%)");
        text.Should().Contain("DiningOut — 3000.00 ₽ (30%)");
        text.Should().Contain("Transport — 1000.00 ₽ (10%)");
        text.IndexOf("Groceries", StringComparison.Ordinal).Should()
            .BeLessThan(text.IndexOf("DiningOut", StringComparison.Ordinal));
        text.IndexOf("DiningOut", StringComparison.Ordinal).Should()
            .BeLessThan(text.IndexOf("Transport", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_top_five_categories_are_listed()
    {
        var rows = Enumerable.Range(1, 7)
            .Select(i => new CategorySpend($"Cat{i}", i * 100m))
            .ToArray();

        var text = StatsTextBuilder.Build(rows, Start, periodsAgo: 0);

        text.Should().Contain("Cat7").And.Contain("Cat3");
        text.Should().NotContain("Cat2").And.NotContain("Cat1");
    }

    [Fact]
    public void Total_line_present()
    {
        var rows = new[] { new CategorySpend("Groceries", 1234.50m) };

        var text = StatsTextBuilder.Build(rows, Start, periodsAgo: 0);

        text.Should().Contain("Всего: 1234.50 ₽");
    }

    [Fact]
    public void Header_mentions_current_period_and_start_date()
    {
        var rows = new[] { new CategorySpend("Groceries", 100m) };

        var text = StatsTextBuilder.Build(rows, Start, periodsAgo: 0);

        text.Should().Contain("текущий период").And.Contain("2026-06-01");
    }

    [Fact]
    public void Header_mentions_periods_ago_for_history()
    {
        var rows = new[] { new CategorySpend("Groceries", 100m) };

        var text = StatsTextBuilder.Build(rows, Start, periodsAgo: 1);

        text.Should().NotContain("текущий период");
        text.Should().Contain("1 назад");
    }

    [Fact]
    public void Shares_are_rounded_to_whole_percent()
    {
        var rows = new[]
        {
            new CategorySpend("A", 1m),
            new CategorySpend("B", 2m),
        };

        var text = StatsTextBuilder.Build(rows, Start, periodsAgo: 0);

        text.Should().Contain("(67%)").And.Contain("(33%)");
    }
}
