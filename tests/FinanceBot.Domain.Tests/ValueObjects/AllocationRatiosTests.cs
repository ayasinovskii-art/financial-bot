using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Domain.Tests.ValueObjects;

public sealed class AllocationRatiosTests
{
    [Fact]
    public void Default_is_50_25_25()
    {
        AllocationRatios.Default.EssentialsPercent.Should().Be(50);
        AllocationRatios.Default.FunPercent.Should().Be(25);
        AllocationRatios.Default.DepositPercent.Should().Be(25);
    }

    [Fact]
    public void Construct_with_invalid_sum_throws()
    {
        FluentActions.Invoking(() => new AllocationRatios(60, 30, 20))
            .Should().Throw<ArgumentException>().WithMessage("*sum to 100*");
    }

    [Fact]
    public void Negative_percent_throws()
    {
        FluentActions.Invoking(() => new AllocationRatios(-1, 50, 51))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(50, 25, 25, 1000, 500, 250, 250)]
    [InlineData(60, 30, 10, 1000, 600, 300, 100)]
    public void ApplyTo_distributes_with_remainder_to_deposit(
        int e, int f, int d, decimal income, decimal eExp, decimal fExp, decimal dExp)
    {
        var ratios = new AllocationRatios(e, f, d);
        var (essentials, fun, deposit) = ratios.ApplyTo(income);
        essentials.Should().Be(eExp);
        fun.Should().Be(fExp);
        deposit.Should().Be(dExp);
        (essentials + fun + deposit).Should().Be(income);
    }

    [Fact]
    public void ApplyTo_handles_rounding_so_deposit_absorbs_remainder()
    {
        // 33/33/34 of 100 → essentials=33, fun=33, deposit=100-66=34
        var (essentials, fun, deposit) = new AllocationRatios(33, 33, 34).ApplyTo(100m);
        essentials.Should().Be(33m);
        fun.Should().Be(33m);
        deposit.Should().Be(34m);
    }
}
