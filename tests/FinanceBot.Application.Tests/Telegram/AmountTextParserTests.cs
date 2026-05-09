using FinanceBot.Application.Actors.Telegram;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Telegram;

public sealed class AmountTextParserTests
{
    [Theory]
    [InlineData("750", null, 750, null)]
    [InlineData("750 обед", null, 750, "обед")]
    [InlineData("750.50 обед в столовой", null, 750.50, "обед в столовой")]
    [InlineData("2025-05-15 1500 продукты", "2025-05-15", 1500, "продукты")]
    [InlineData("750,50", null, 750.50, null)]
    public void TryParseSingle_recognises_supported_shapes(string input, string? expectedDate, decimal amount, string? description)
    {
        var parsed = AmountTextParser.TryParseSingle(input);
        parsed.Should().NotBeNull();
        parsed!.Amount.Should().Be(amount);
        parsed.Description.Should().Be(description);
        if (expectedDate is null)
        {
            parsed.Date.Should().BeNull();
        }
        else
        {
            parsed.Date.Should().Be(DateOnly.Parse(expectedDate, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("обед")]
    [InlineData("0")]
    [InlineData("-100")]
    public void TryParseSingle_rejects_invalid(string input)
    {
        AmountTextParser.TryParseSingle(input).Should().BeNull();
    }

    [Fact]
    public void ParseMultiple_with_plus_separator()
    {
        var result = AmountTextParser.ParseMultiple("750 обед + 400 такси");
        result.Should().HaveCount(2);
        result[0].Amount.Should().Be(750);
        result[0].Description.Should().Be("обед");
        result[1].Amount.Should().Be(400);
        result[1].Description.Should().Be("такси");
    }

    [Fact]
    public void ParseMultiple_with_comma_separator()
    {
        var result = AmountTextParser.ParseMultiple("750 обед, 400 такси");
        result.Should().HaveCount(2);
    }

    [Fact]
    public void ParseMultiple_returns_empty_if_any_segment_invalid()
    {
        var result = AmountTextParser.ParseMultiple("750 обед + просто текст");
        result.Should().BeEmpty();
    }
}
