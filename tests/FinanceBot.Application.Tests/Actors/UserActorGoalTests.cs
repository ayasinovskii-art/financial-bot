using Akka.Actor;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class UserActorGoalTests : AkkaPersistenceTestBase
{
    [Fact]
    public void AddGoal_persists_GoalAdded_and_replies_GoalAccepted()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        var goalId = Guid.NewGuid();
        actor.Tell(new AddGoal(userId, goalId, "Скальная школа в горах", TargetAmount: 80_000m, TargetDate: new DateOnly(2027, 8, 1)));

        var reply = ExpectMsg<GoalAccepted>();
        reply.GoalId.Should().Be(goalId);
    }

    [Fact]
    public void AddGoal_without_registration_replies_GoalRejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new AddGoal(userId, Guid.NewGuid(), "Цель без регистрации", null, null));

        ExpectMsg<GoalRejected>().Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetUserGoals_returns_UserGoalsList_without_persisting()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        var goalId = Guid.NewGuid();
        actor.Tell(new AddGoal(userId, goalId, "Путешествие в Японию", null, null));
        ExpectMsg<GoalAccepted>();

        actor.Tell(new GetUserGoals(userId));
        var list = ExpectMsg<UserGoalsList>();

        list.Goals.Should().ContainSingle(g => g.GoalId == goalId && !g.IsCompleted);
    }

    [Fact]
    public void GetUserGoals_on_fresh_actor_returns_empty_list()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new GetUserGoals(userId));
        var list = ExpectMsg<UserGoalsList>();

        list.Goals.Should().BeEmpty();
    }

    [Fact]
    public void CompleteGoal_persists_GoalCompleted_and_replies_GoalCompletedReply()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        var goalId = Guid.NewGuid();
        actor.Tell(new AddGoal(userId, goalId, "Новый ноутбук", null, null));
        ExpectMsg<GoalAccepted>();

        actor.Tell(new CompleteGoal(userId, goalId));
        var reply = ExpectMsg<GoalCompletedReply>();
        reply.GoalId.Should().Be(goalId);
    }

    [Fact]
    public void CompleteGoal_with_unknown_GoalId_replies_GoalRejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new CompleteGoal(userId, Guid.NewGuid()));

        ExpectMsg<GoalRejected>().Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CompleteGoal_already_completed_replies_GoalRejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        var goalId = Guid.NewGuid();
        actor.Tell(new AddGoal(userId, goalId, "Курс по программированию", null, null));
        ExpectMsg<GoalAccepted>();

        actor.Tell(new CompleteGoal(userId, goalId));
        ExpectMsg<GoalCompletedReply>();

        actor.Tell(new CompleteGoal(userId, goalId));
        ExpectMsg<GoalRejected>().Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Goals_state_reflects_completed_goal_after_CompleteGoal()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        var goalId = Guid.NewGuid();
        actor.Tell(new AddGoal(userId, goalId, "Велосипед", null, null));
        ExpectMsg<GoalAccepted>();

        actor.Tell(new CompleteGoal(userId, goalId));
        ExpectMsg<GoalCompletedReply>();

        actor.Tell(new GetUserGoals(userId));
        var list = ExpectMsg<UserGoalsList>();

        list.Goals.Should().ContainSingle(g => g.GoalId == goalId && g.IsCompleted);
    }
}
