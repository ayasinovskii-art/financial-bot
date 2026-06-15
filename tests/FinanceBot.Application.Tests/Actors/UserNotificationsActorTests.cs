using Akka.Actor;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Domain.Events.Notifications;
using FinanceBot.Domain.Events.Scheduling;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

/// <summary>
/// Юнит-тесты для UserNotificationsActor: anti-spam лимит, текстовые билдеры, recovery.
/// </summary>
public sealed class UserNotificationsActorTests : AkkaPersistenceTestBase
{
    [Fact]
    public void LargeExpense_text_contains_percentage_and_bucket()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserNotificationsActor.CreateProps(userId));
        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        actor.Tell(new EnrichedProactiveTrigger("large_expense", "Fun", 500m, 1000m, 12345L));

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        reply.ChatId.Should().Be(12345L);
        reply.Text.Should().Contain("50%");
        reply.Text.Should().Contain("Fun");
    }

    [Fact]
    public void BucketNearLimit_text_contains_percentage_and_bucket()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserNotificationsActor.CreateProps(userId));
        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        actor.Tell(new EnrichedProactiveTrigger("bucket_near_limit", "Essentials", 900m, 1000m, 12345L));

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        reply.ChatId.Should().Be(12345L);
        reply.Text.Should().Contain("90%");
        reply.Text.Should().Contain("Essentials");
    }

    [Fact]
    public void WeeklyDigest_publishes_reply()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserNotificationsActor.CreateProps(userId));
        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        actor.Tell(new EnrichedWeeklyDigestTick(new WeeklyDigestTickFired(userId, DateTimeOffset.UtcNow), 77777L));

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        reply.ChatId.Should().Be(77777L);
        reply.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DailyLimit_blocks_fourth_notification()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserNotificationsActor.CreateProps(userId));
        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        for (var i = 0; i < UserNotificationsActor.DailyLimit; i++)
        {
            actor.Tell(new EnrichedProactiveTrigger("large_expense", "Fun", 300m, 1000m, 555L));
            ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        }

        // Fourth message must be silently dropped.
        actor.Tell(new EnrichedProactiveTrigger("large_expense", "Fun", 300m, 1000m, 555L));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void Recovery_restores_sentCounts_and_respects_daily_limit()
    {
        var userId = Guid.NewGuid();
        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        // Phase 1: send DailyLimit-1 notifications so the first actor persists them.
        var actor1 = Sys.ActorOf(UserNotificationsActor.CreateProps(userId), $"notif-{userId:N}-1");
        for (var i = 0; i < UserNotificationsActor.DailyLimit - 1; i++)
        {
            actor1.Tell(new EnrichedProactiveTrigger("large_expense", "Fun", 300m, 1000m, 111L));
            ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        }

        // Stop actor1 and wait until it terminates (ensures persistence is complete).
        Watch(actor1);
        Sys.Stop(actor1);
        ExpectTerminated(actor1, TimeSpan.FromSeconds(5));

        // Phase 2: restart with same userId (same PersistenceId, same journal).
        var actor2 = Sys.ActorOf(UserNotificationsActor.CreateProps(userId), $"notif-{userId:N}-2");

        // One more notification fits within the daily limit.
        actor2.Tell(new EnrichedProactiveTrigger("large_expense", "Fun", 300m, 1000m, 111L));
        ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));

        // The very next one must be suppressed — limit is now reached.
        actor2.Tell(new EnrichedProactiveTrigger("large_expense", "Fun", 300m, 1000m, 111L));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void Unknown_trigger_kind_publishes_nothing()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserNotificationsActor.CreateProps(userId));
        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        actor.Tell(new EnrichedProactiveTrigger("unknown_kind", "Fun", 100m, 1000m, 999L));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
}
