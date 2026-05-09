using FinanceBot.Domain.ValueObjects;
using FinanceBot.Infrastructure.CategoryRules;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Categorizer;

public sealed class JsonCategoryRulesTests
{
    private readonly JsonCategoryRules _rules = new();

    [Theory]
    [InlineData("обед", Category.DiningOut)]
    [InlineData("Обед в столовой", Category.DiningOut)]
    [InlineData("такси домой", Category.Transport)]
    [InlineData("uber до офиса", Category.Transport)]
    [InlineData("аптека рядом с домом", Category.Health)]
    [InlineData("лекарство", Category.Health)]
    [InlineData("netflix подписка", Category.Subscriptions)]
    [InlineData("кино с другом", Category.Entertainment)]
    [InlineData("квартплата", Category.Utilities)]
    [InlineData("курс на skillbox", Category.Education)]
    [InlineData("zara куртка", Category.Clothing)]
    [InlineData("парикмахер", Category.Personal)]
    [InlineData("подарок маме", Category.Gifts)]
    [InlineData("hotel в Сочи", Category.Travel)]
    [InlineData("продукты в магните", Category.Groceries)]
    public void Match_returns_expected_category(string description, Category expected)
    {
        var normalized = NormalizedDescription.FromRaw(description);
        var result = _rules.Match(normalized);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("какая-то фигня")]
    [InlineData("просто текст")]
    [InlineData("")]
    public void Match_returns_null_when_no_keyword(string description)
    {
        var normalized = NormalizedDescription.FromRaw(description);
        _rules.Match(normalized).Should().BeNull();
    }
}
