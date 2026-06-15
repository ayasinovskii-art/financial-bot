using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Commands;
using FinanceBot.Application.Actors.Telegram.Commands.Handlers;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class GoalHandlerTests : AkkaPersistenceTestBase
{
    private static readonly long TestChatId = 42L;
    private static readonly long TestTelegramId = 100L;

    private TelegramCommandContext MakeCtx(string argumentLine, IActorRef shardRef)
    {
        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(shardRef);

        return new TelegramCommandContext
        {
            Update = new IncomingTelegramUpdate(1L, TestChatId, TestTelegramId, null, "Test", null,
                "/goal " + argumentLine, DateTimeOffset.UtcNow),
            ArgumentLine = argumentLine,
            Allowed = new AccessDecision.Allowed(TestTelegramId, AccessRole.User),
            Self = TestActor,
            System = Sys,
            Log = Logging.GetLogger(Sys, "GoalHandlerTest"),
            Defaults = new UserDefaultsOptions(),
            AskTimeout = TimeSpan.FromSeconds(5)
        };
    }

    [Fact]
    public void Unknown_subcommand_replies_GoalUsage_without_touching_shard()
    {
        var shard = CreateTestProbe();
        var ctx = MakeCtx("xyz", shard.Ref);

        new GoalHandler().Execute(ctx);

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(2));
        reply.ChatId.Should().Be(TestChatId);
        reply.Text.Should().Contain("add");
        reply.Text.Should().Contain("list");
        reply.Text.Should().Contain("done");
        shard.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Add_with_empty_description_replies_usage_hint_without_touching_shard()
    {
        var shard = CreateTestProbe();
        var ctx = MakeCtx("add ", shard.Ref);

        new GoalHandler().Execute(ctx);

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(2));
        reply.ChatId.Should().Be(TestChatId);
        reply.Text.Should().Contain("add");
        shard.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Add_with_whitespace_only_description_replies_usage_hint()
    {
        var shard = CreateTestProbe();
        var ctx = MakeCtx("add    ", shard.Ref);

        new GoalHandler().Execute(ctx);

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(2));
        reply.Text.Should().Contain("add");
    }

    [Fact]
    public void Add_with_description_asks_shard_and_forwards_GoalAccepted_reply()
    {
        var shard = CreateTestProbe();
        var ctx = MakeCtx("add Скальная школа в горах", shard.Ref);

        new GoalHandler().Execute(ctx);

        var envelope = shard.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(2));
        var addGoal = envelope.Message.Should().BeOfType<AddGoal>().Subject;
        addGoal.Description.Should().Be("Скальная школа в горах");

        var goalId = Guid.NewGuid();
        shard.Reply(new GoalAccepted(goalId));

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(2));
        var replies = completed.Outgoing.OfType<OutgoingTelegramReply>().ToList();
        replies.Should().ContainSingle();
        replies[0].ChatId.Should().Be(TestChatId);
        replies[0].Text.Should().Contain(goalId.ToString());
    }

    [Fact]
    public void Done_with_non_numeric_index_replies_format_hint_without_touching_shard()
    {
        var shard = CreateTestProbe();
        var ctx = MakeCtx("done abc", shard.Ref);

        new GoalHandler().Execute(ctx);

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(2));
        reply.Text.Should().Contain("done");
        shard.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Done_with_zero_index_replies_format_hint()
    {
        var shard = CreateTestProbe();
        var ctx = MakeCtx("done 0", shard.Ref);

        new GoalHandler().Execute(ctx);

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(2));
        reply.Text.Should().Contain("done");
    }

    [Fact]
    public void Done_with_out_of_range_index_replies_GoalNotFound()
    {
        var shard = CreateTestProbe();
        var ctx = MakeCtx("done 5", shard.Ref);

        new GoalHandler().Execute(ctx);

        var goalsEnvelope = shard.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(2));
        goalsEnvelope.Message.Should().BeOfType<GetUserGoals>();

        var goalId = Guid.NewGuid();
        shard.Reply(new UserGoalsList([new GoalState(goalId, "Одна цель", null, null, IsCompleted: false)]));

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(3));
        var replies = completed.Outgoing.OfType<OutgoingTelegramReply>().ToList();
        replies.Should().ContainSingle();
        replies[0].ChatId.Should().Be(TestChatId);
        replies[0].Text.Should().Contain("list");
    }

    [Fact]
    public void Done_with_valid_index_sends_CompleteGoal_and_replies_GoalDone()
    {
        var shard = CreateTestProbe();
        var ctx = MakeCtx("done 1", shard.Ref);

        new GoalHandler().Execute(ctx);

        var goalsEnvelope = shard.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(2));
        goalsEnvelope.Message.Should().BeOfType<GetUserGoals>();

        var goalId = Guid.NewGuid();
        shard.Reply(new UserGoalsList([new GoalState(goalId, "Скальная школа", null, null, IsCompleted: false)]));

        var completeEnvelope = shard.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(2));
        var completeGoal = completeEnvelope.Message.Should().BeOfType<CompleteGoal>().Subject;
        completeGoal.GoalId.Should().Be(goalId);

        shard.Reply(new GoalCompletedReply(goalId));

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(2));
        completed.Outgoing.OfType<OutgoingTelegramReply>().Should().ContainSingle(r =>
            r.ChatId == TestChatId && !string.IsNullOrWhiteSpace(r.Text));
    }

    [Fact]
    public void Done_skips_completed_goals_when_resolving_index()
    {
        var shard = CreateTestProbe();
        var ctx = MakeCtx("done 1", shard.Ref);

        new GoalHandler().Execute(ctx);

        shard.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(2));

        var completedGoalId = Guid.NewGuid();
        var activeGoalId = Guid.NewGuid();
        shard.Reply(new UserGoalsList([
            new GoalState(completedGoalId, "Завершённая", null, null, IsCompleted: true),
            new GoalState(activeGoalId, "Активная", null, null, IsCompleted: false)
        ]));

        var completeEnvelope = shard.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(2));
        ((CompleteGoal)completeEnvelope.Message).GoalId.Should().Be(activeGoalId);

        shard.Reply(new GoalCompletedReply(activeGoalId));

        ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(2));
    }
}
