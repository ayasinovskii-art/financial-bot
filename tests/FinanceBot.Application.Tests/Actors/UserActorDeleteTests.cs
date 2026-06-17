using Akka.Actor;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class UserActorDeleteTests : AkkaPersistenceTestBase
{
    [Fact]
    public void DeleteExpense_persists_ExpenseDeleted_and_replies_DeletedSuccessfully()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new ReportIncome(userId, 50_000m, DateTimeOffset.UtcNow, null));
        ExpectMsg<IncomeAccepted>();

        actor.Tell(new ReportExpense(userId, 700m, DateTimeOffset.UtcNow, "Обед", ExpenseSource.Manual));
        var accepted = ExpectMsg<ExpenseAccepted>();

        actor.Tell(new DeleteExpense(userId, accepted.ExpenseId, "тест"));
        ExpectMsg<DeletedSuccessfully>();
    }

    [Fact]
    public void DeleteExpense_with_empty_guid_replies_DeleteRejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new DeleteExpense(userId, Guid.Empty, "тест"));
        var reply = ExpectMsg<DeleteRejected>();
        reply.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DeleteIncome_persists_IncomeDeleted_and_replies_DeletedSuccessfully()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new ReportIncome(userId, 50_000m, DateTimeOffset.UtcNow, null));
        var incomeAccepted = ExpectMsg<IncomeAccepted>();

        actor.Tell(new DeleteIncome(userId, incomeAccepted.IncomeId));
        ExpectMsg<DeletedSuccessfully>();
    }

    [Fact]
    public void DeleteIncome_with_empty_guid_replies_DeleteRejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new DeleteIncome(userId, Guid.Empty));
        var reply = ExpectMsg<DeleteRejected>();
        reply.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RemoveGoal_persists_GoalRemoved_and_replies_DeletedSuccessfully()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        var goalId = Guid.NewGuid();
        actor.Tell(new AddGoal(userId, goalId, "Купить машину", null, null));
        ExpectMsg<GoalAccepted>();

        actor.Tell(new RemoveGoal(userId, goalId));
        ExpectMsg<DeletedSuccessfully>();
    }

    [Fact]
    public void RemoveGoal_for_unknown_GoalId_replies_GoalRejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new RemoveGoal(userId, Guid.NewGuid()));
        var reply = ExpectMsg<GoalRejected>();
        reply.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RemoveGoal_removes_goal_from_state()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        var goalId = Guid.NewGuid();
        actor.Tell(new AddGoal(userId, goalId, "Отпуск в Японии", null, null));
        ExpectMsg<GoalAccepted>();

        actor.Tell(new RemoveGoal(userId, goalId));
        ExpectMsg<DeletedSuccessfully>();

        actor.Tell(new GetUserGoals(userId));
        var list = ExpectMsg<UserGoalsList>();
        list.Goals.Should().BeEmpty();
    }
}
