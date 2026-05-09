using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Domain.Tests.ValueObjects;

public sealed class NormalizedDescriptionTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Обед", "обед")]
    [InlineData("  Обед  ", "обед")]
    [InlineData("Обед   В   Столовой", "обед в столовой")]
    [InlineData("Uber\tдо\nофиса", "uber до офиса")]
    public void FromRaw_normalizes(string? input, string expected)
    {
        var result = NormalizedDescription.FromRaw(input);
        result.Value.Should().Be(expected);
    }

    [Fact]
    public void Empty_input_yields_IsEmpty_true()
    {
        NormalizedDescription.FromRaw("").IsEmpty.Should().BeTrue();
        NormalizedDescription.FromRaw("обед").IsEmpty.Should().BeFalse();
    }
}
