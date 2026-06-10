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

    // --- Короткие форматы (issue #2): суффиксы валюты ---

    [Theory]
    [InlineData("500р", 500)]
    [InlineData("500 ₽", 500)]
    [InlineData("500руб", 500)]
    [InlineData("500 руб.", 500)]
    [InlineData("500Р", 500)]
    [InlineData("120.50р", 120.50)]
    public void TryParseSingle_accepts_currency_suffix(string input, decimal amount)
    {
        var parsed = AmountTextParser.TryParseSingle(input);
        parsed.Should().NotBeNull();
        parsed!.Amount.Should().Be(amount);
        parsed.Description.Should().BeNull("суффикс валюты не должен попадать в описание");
    }

    [Fact]
    public void TryParseSingle_currency_suffix_keeps_description()
    {
        var parsed = AmountTextParser.TryParseSingle("500р обед");
        parsed.Should().NotBeNull();
        parsed!.Amount.Should().Be(500);
        parsed.Description.Should().Be("обед");
    }

    [Fact]
    public void TryParseSingle_does_not_eat_description_starting_with_r()
    {
        // «рис» начинается на «р», но это описание, а не валюта
        var parsed = AmountTextParser.TryParseSingle("750 рис");
        parsed.Should().NotBeNull();
        parsed!.Amount.Should().Be(750);
        parsed.Description.Should().Be("рис");
    }

    // --- Короткие форматы (issue #2): множитель к/k ---

    [Theory]
    [InlineData("1.5к", 1500)]
    [InlineData("1,5к", 1500)]
    [InlineData("1.5k", 1500)]
    [InlineData("1.5K", 1500)]
    [InlineData("2к", 2000)]
    [InlineData("1500", 1500)]
    public void TryParseSingle_accepts_thousands_multiplier(string input, decimal amount)
    {
        var parsed = AmountTextParser.TryParseSingle(input);
        parsed.Should().NotBeNull();
        parsed!.Amount.Should().Be(amount);
    }

    [Fact]
    public void TryParseSingle_detached_k_is_description_not_multiplier()
    {
        // «к» — частый русский предлог; множителем считается только приклеенный суффикс
        var parsed = AmountTextParser.TryParseSingle("750 к чаю");
        parsed.Should().NotBeNull();
        parsed!.Amount.Should().Be(750);
        parsed.Description.Should().Be("к чаю");
    }

    // --- Короткие форматы (issue #2): пробел как разделитель тысяч ---

    [Theory]
    [InlineData("2 000", 2000, null)]
    [InlineData("2 000", 2000, null)]   // неразрывный пробел
    [InlineData("2 000,50", 2000.50, null)]
    [InlineData("12 345 678", 12345678, null)]
    [InlineData("2 000 продукты", 2000, "продукты")]
    public void TryParseSingle_accepts_space_thousands_separator(string input, decimal amount, string? description)
    {
        var parsed = AmountTextParser.TryParseSingle(input);
        parsed.Should().NotBeNull();
        parsed!.Amount.Should().Be(amount);
        parsed.Description.Should().Be(description);
    }

    // --- Короткие форматы (issue #2): сумма в конце сегмента ---

    [Fact]
    public void ParseMultiple_parses_description_first_segments()
    {
        var result = AmountTextParser.ParseMultiple("обед 750 + такси 1.5к");
        result.Should().HaveCount(2);
        result[0].Amount.Should().Be(750);
        result[0].Description.Should().Be("обед");
        result[1].Amount.Should().Be(1500);
        result[1].Description.Should().Be("такси");
    }

    [Fact]
    public void ParseMultiple_does_not_split_on_decimal_comma()
    {
        var result = AmountTextParser.ParseMultiple("2 000,50 продукты");
        result.Should().HaveCount(1);
        result[0].Amount.Should().Be(2000.50m);
        result[0].Description.Should().Be("продукты");
    }

    // --- Регрессии: невалидное по-прежнему отклоняется ---

    [Theory]
    [InlineData("abc")]
    [InlineData("750abc")]
    [InlineData("0к")]
    [InlineData("-1.5к")]
    public void TryParseSingle_rejects_invalid_short_formats(string input)
    {
        AmountTextParser.TryParseSingle(input).Should().BeNull();
    }

    [Theory]
    [InlineData("500р", 500)]
    [InlineData("1.5к", 1500)]
    [InlineData("2 000", 2000)]
    public void TryParseAmount_supports_short_formats(string token, decimal expected)
    {
        AmountTextParser.TryParseAmount(token, out var amount).Should().BeTrue();
        amount.Should().Be(expected);
    }

    [Fact]
    public void TryParseAmount_rejects_token_with_trailing_garbage()
    {
        AmountTextParser.TryParseAmount("500хлеб", out _).Should().BeFalse();
    }
}
