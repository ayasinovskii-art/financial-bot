using Akka.Actor;
using FinanceBot.Application.Actors.Categorizer;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.Events.Scheduling;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

/// <summary>Stage 17: проверка FSM Idle ↔ AwaitingDailyExpenses.</summary>
public sealed class UserActorEveningFlowTests : AkkaPersistenceTestBase
{
    [Fact]
    public void EveningTickFired_publishes_question_to_event_stream()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 12345, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));
        actor.Tell(new EveningTickFired(userId, DateTimeOffset.UtcNow));

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        reply.ChatId.Should().Be(12345);
        reply.Text.Should().Contain("Вечерний опрос");
        reply.Text.Should().Contain("Ответь свободным текстом");
    }

    [Fact]
    public void Cancel_during_evening_flow_exits_FSM_and_unstashes()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 12345, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));
        actor.Tell(new EveningTickFired(userId, DateTimeOffset.UtcNow));
        ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));

        // В FSM AwaitingDailyExpenses посылаем GetUserSnapshot — должен застэшиться,
        // потому что Stage 17 разрешает только ReportExpense / Cancel / SilenceDeadlineFired.
        actor.Tell(new GetUserSnapshot(userId));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Cancel — FSM выходит, unstash → застэшенный GetUserSnapshot обрабатывается.
        actor.Tell(new Cancel(userId));
        ExpectMsg<CancelAcknowledged>();
        var snap = ExpectMsg<UserSnapshot>(TimeSpan.FromSeconds(2));
        snap.UserId.Should().Be(userId);
    }

    [Fact]
    public void SilenceDeadline_with_auto_confirm_off_replies_skip_message()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 9999, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));
        actor.Tell(new EveningTickFired(userId, DateTimeOffset.UtcNow));
        var question = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        question.Text.Should().Contain("Вечерний опрос");

        actor.Tell(new SilenceDeadlineFired(userId, DateTimeOffset.UtcNow));
        var skip = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        skip.Text.Should().Contain("пропустил день");
    }

    [Fact]
    public void EveningTick_without_registration_is_silently_ignored()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));
        actor.Tell(new EveningTickFired(userId, DateTimeOffset.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void ReportExpense_in_FSM_processes_and_exits()
    {
        // Зарегистрируем категоризатор и создадим period (через ReportIncome).
        var rules = new StubRules(matched: null);
        var categorizer = Sys.ActorOf(CategorizerActor.CreateProps(rules), "cat-evening-exit");
        Akka.Hosting.ActorRegistry.For(Sys).Register<CategorizerActorMarker>(categorizer);

        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 7777, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();
        actor.Tell(new ReportIncome(userId, 100_000m, DateTimeOffset.UtcNow, null));
        ExpectMsg<IncomeAccepted>();

        Sys.EventStream.Subscribe(TestActor, typeof(OutgoingTelegramReply));
        actor.Tell(new EveningTickFired(userId, DateTimeOffset.UtcNow));
        ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));

        // Шлём ReportExpense в FSM — должен обработаться + актор вернётся в Idle.
        actor.Tell(new ReportExpense(userId, 750m, DateTimeOffset.UtcNow, "обед", ExpenseSource.Manual));
        ExpectMsg<ExpenseAccepted>(TimeSpan.FromSeconds(3));

        // После выхода из FSM — обычные команды снова работают сразу.
        actor.Tell(new GetUserSnapshot(userId));
        ExpectMsg<UserSnapshot>();
    }

    private sealed class StubRules(Category? matched) : ICategoryRules
    {
        public Category? Match(NormalizedDescription description) => matched;
    }
}
