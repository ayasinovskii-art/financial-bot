using Akka.Actor;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.Events.Scheduling;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

/// <summary>
/// Проверяет, что UserActor корректно вызывает UserNotificationsActor при тратах и WeeklyDigestTick.
/// </summary>
public sealed class UserActorNotificationsTests : AkkaPersistenceTestBase
{
    private const long TelegramId = 99999L;

    // Income that yields AllocationEssentials = 5000, AllocationFun = 2500, AllocationDeposit = 2500.
    private const decimal TestIncome = 10_000m;

    // Category.Other maps to Bucket.Essentials (AllocationEssentials = 5000).
    // >20% of 5000 = >1000 — triggers large_expense.
    private const decimal LargeExpenseAmount = 1_100m;

    private static IActorRef SetupRegisteredUserWithIncome(
        AkkaPersistenceTestBase testKit, Guid userId, bool notificationsEnabled)
    {
        var actor = testKit.Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, TelegramId, "UTC"));
        testKit.ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new ReportIncome(userId, TestIncome, DateTimeOffset.UtcNow, null));
        testKit.ExpectMsg<IncomeAccepted>();

        if (notificationsEnabled)
        {
            actor.Tell(new UpdateSettings(userId, SettingsKey.NotificationsEnabled, "true"));
            testKit.ExpectMsg<SettingsUpdated>();
        }

        return actor;
    }

    [Fact]
    public void LargeExpense_trigger_fires_when_notifications_enabled()
    {
        var userId = Guid.NewGuid();
        var actor = SetupRegisteredUserWithIncome(this, userId, notificationsEnabled: true);

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        actor.Tell(new ReportExpense(userId, LargeExpenseAmount, DateTimeOffset.UtcNow, "обед", ExpenseSource.Manual));
        ExpectMsg<ExpenseAccepted>(TimeSpan.FromSeconds(3));

        // Notifications actor publishes proactive reply asynchronously.
        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        reply.ChatId.Should().Be(TelegramId);
        reply.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Notifications_disabled_suppresses_large_expense_trigger()
    {
        var userId = Guid.NewGuid();
        // notifications_enabled defaults to false — do NOT enable it.
        var actor = SetupRegisteredUserWithIncome(this, userId, notificationsEnabled: false);

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        actor.Tell(new ReportExpense(userId, LargeExpenseAmount, DateTimeOffset.UtcNow, "обед", ExpenseSource.Manual));
        ExpectMsg<ExpenseAccepted>(TimeSpan.FromSeconds(3));

        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void BucketNearLimit_trigger_fires_when_80pct_spent()
    {
        var userId = Guid.NewGuid();
        var actor = SetupRegisteredUserWithIncome(this, userId, notificationsEnabled: true);

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        // AllocationEssentials = 5000.  Need spent >= 4000 without triggering large_expense.
        // Each expense must be <= 1000 (20% of 5000 exactly, so NOT > 20%).
        // After 4 expenses of 1000 each: spent = 4000, 4000/5000 = 0.8 → bucket_near_limit.
        const decimal perExpense = 1_000m;
        for (var i = 0; i < 3; i++)
        {
            actor.Tell(new ReportExpense(userId, perExpense, DateTimeOffset.UtcNow, "покупка", ExpenseSource.Manual));
            ExpectMsg<ExpenseAccepted>(TimeSpan.FromSeconds(3));
            // Spent < 80 % after first three — no notification yet.
            ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        }

        // Fourth expense pushes spent to 4000 = 80 % → notification expected.
        actor.Tell(new ReportExpense(userId, perExpense, DateTimeOffset.UtcNow, "покупка", ExpenseSource.Manual));
        ExpectMsg<ExpenseAccepted>(TimeSpan.FromSeconds(3));

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        reply.ChatId.Should().Be(TelegramId);
        reply.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WeeklyDigestTick_fires_reply_when_notifications_enabled()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, TelegramId, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new UpdateSettings(userId, SettingsKey.NotificationsEnabled, "true"));
        ExpectMsg<SettingsUpdated>();

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        actor.Tell(new WeeklyDigestTickFired(userId, DateTimeOffset.UtcNow));

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        reply.ChatId.Should().Be(TelegramId);
        reply.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WeeklyDigestTick_suppressed_when_notifications_disabled()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, TelegramId, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));

        actor.Tell(new WeeklyDigestTickFired(userId, DateTimeOffset.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
}
