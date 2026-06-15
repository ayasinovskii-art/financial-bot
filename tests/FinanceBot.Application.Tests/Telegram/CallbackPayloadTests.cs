using FinanceBot.Application.Telegram;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Telegram;

public sealed class CallbackPayloadTests
{
    private static readonly Guid SomeId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    [Fact]
    public void Encode_with_shortArg_produces_correct_format()
    {
        var result = CallbackPayload.Encode("correct", SomeId, "Groceries");
        result.Should().Be($"correct:{SomeId:N}:Groceries");
    }

    [Fact]
    public void Encode_without_shortArg_omits_third_segment()
    {
        var result = CallbackPayload.Encode("correct", SomeId);
        result.Should().Be($"correct:{SomeId:N}");
    }

    [Fact]
    public void Encode_throws_when_exceeds_64_bytes()
    {
        var longAction = new string('x', 64);
        var act = () => CallbackPayload.Encode(longAction, SomeId);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryDecode_roundtrips_Encode_with_shortArg()
    {
        var encoded = CallbackPayload.Encode("correct", SomeId, "Food");
        var ok = CallbackPayload.TryDecode(encoded, out var action, out var entityId, out var shortArg);
        ok.Should().BeTrue();
        action.Should().Be("correct");
        entityId.Should().Be(SomeId);
        shortArg.Should().Be("Food");
    }

    [Fact]
    public void TryDecode_roundtrips_Encode_without_shortArg()
    {
        var encoded = CallbackPayload.Encode("del", SomeId);
        var ok = CallbackPayload.TryDecode(encoded, out var action, out var entityId, out var shortArg);
        ok.Should().BeTrue();
        action.Should().Be("del");
        entityId.Should().Be(SomeId);
        shortArg.Should().BeNull();
    }

    [Fact]
    public void TryDecode_returns_false_for_empty()
    {
        CallbackPayload.TryDecode(string.Empty, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDecode_returns_false_for_missing_colon()
    {
        CallbackPayload.TryDecode("nocolan", out _, out _, out _).Should().BeFalse();
    }
}
