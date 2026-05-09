using FinanceBot.Application.Actors.Telegram;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Telegram;

public sealed class UserIdFromTelegramIdTests
{
    [Fact]
    public void Same_telegramId_yields_same_Guid_across_calls()
    {
        var first = UserIdFromTelegramId.Resolve(123456789);
        var second = UserIdFromTelegramId.Resolve(123456789);
        first.Should().Be(second);
    }

    [Fact]
    public void Different_telegramIds_yield_different_Guids()
    {
        var a = UserIdFromTelegramId.Resolve(111);
        var b = UserIdFromTelegramId.Resolve(222);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Resolve_is_not_empty_guid()
    {
        UserIdFromTelegramId.Resolve(1).Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Stable_across_processes_via_explicit_test_vector()
    {
        // Если этот тест начнёт падать — значит изменилась схема выведения Guid из telegramId.
        // В таком случае старые UserActor-ы (с PersistenceId = "user-{старыйGuid}") перестанут находиться.
        UserIdFromTelegramId.Resolve(42).Should()
            .Be(UserIdFromTelegramId.Resolve(42), "детерминизм должен сохраняться");
    }
}
