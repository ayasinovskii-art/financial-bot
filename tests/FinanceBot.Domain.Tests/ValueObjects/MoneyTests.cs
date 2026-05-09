using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Domain.Tests.ValueObjects;

public sealed class MoneyTests
{
    [Fact]
    public void DefaultCurrency_is_RUB()
    {
        var money = new Money(100m);
        money.Currency.Should().Be("RUB");
    }

    [Fact]
    public void Add_same_currency_succeeds()
    {
        var a = new Money(100m);
        var b = new Money(50m);
        a.Add(b).Should().Be(new Money(150m));
    }

    [Fact]
    public void Add_different_currency_throws()
    {
        var rub = new Money(100m, "RUB");
        var usd = new Money(50m, "USD");
        FluentActions.Invoking(() => rub.Add(usd))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Currency mismatch*");
    }

    [Fact]
    public void Multiply_keeps_currency()
    {
        var m = new Money(100m, "USD").Multiply(2.5m);
        m.Should().Be(new Money(250m, "USD"));
    }

    [Theory]
    [InlineData(0, true, false, false)]
    [InlineData(1, false, true, false)]
    [InlineData(-1, false, false, true)]
    public void IsZero_IsPositive_IsNegative_match(int amount, bool zero, bool pos, bool neg)
    {
        var m = new Money(amount);
        m.IsZero.Should().Be(zero);
        m.IsPositive.Should().Be(pos);
        m.IsNegative.Should().Be(neg);
    }
}
