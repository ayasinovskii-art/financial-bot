using Akka.Actor;
using FinanceBot.Application.Actors.Scheduler;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

/// <summary>Stage 18: WakeupCheck flow.</summary>
public sealed class UserActorWakeupTests : AkkaPersistenceTestBase
{
    [Fact]
    public void WakeupCheck_publishes_notification_and_persists()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 555, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        var from = DateTimeOffset.UtcNow.AddHours(-2);
        var to = DateTimeOffset.UtcNow.AddMinutes(-5);
        actor.Tell(new WakeupCheck(userId, from, to, ["вечерний опрос 2026-05-08", "зарплата 2026-05-09"]));

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(2));
        reply.ChatId.Should().Be(555);
        reply.Text.Should().Contain("вечерний опрос");
        reply.Text.Should().Contain("зарплата");
        reply.Text.Should().Contain("Я был недоступен");
    }

    [Fact]
    public void Duplicate_WakeupCheck_for_same_downtime_is_suppressed()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 666, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddMinutes(-1);
        actor.Tell(new WakeupCheck(userId, from, to, ["вечерний опрос 2026-05-08"]));
        ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(2));

        // Повторный WakeupCheck с тем же или более ранним downtimeFrom — не должен дублироваться.
        actor.Tell(new WakeupCheck(userId, from, to, ["вечерний опрос 2026-05-08"]));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void WakeupCheck_with_empty_missed_items_still_replies()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 777, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        actor.Tell(new WakeupCheck(userId,
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, []));
        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(2));
        reply.Text.Should().Contain("Пропущенных тиков нет");
    }

    [Fact]
    public void WakeupCheck_for_unregistered_user_is_ignored()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));
        actor.Tell(new WakeupCheck(userId,
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, ["item"]));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
}
