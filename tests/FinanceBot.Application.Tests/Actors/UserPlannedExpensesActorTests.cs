using Akka.Actor;
using FinanceBot.Application.Actors.UserPlannedExpenses;
using FinanceBot.Application.Actors.UserPlannedExpenses.Messages;
using FinanceBot.Domain.Commands.Planned;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class UserPlannedExpensesActorTests : AkkaPersistenceTestBase
{
    [Fact]
    public void Add_planned_succeeds()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserPlannedExpensesActor.CreateProps(userId));

        actor.Tell(new AddPlanned(userId, 30000m, new DateOnly(2026, 6, 15), "rent"));
        var reply = ExpectMsg<PlannedAdded>();
        reply.Plan.Amount.Should().Be(30000m);
        reply.Plan.Status.Should().Be(PlannedStatus.Active);
    }

    [Fact]
    public void Confirm_planned_marks_confirmed()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserPlannedExpensesActor.CreateProps(userId));

        actor.Tell(new AddPlanned(userId, 100m, new DateOnly(2026, 6, 15), "x"));
        var added = ExpectMsg<PlannedAdded>();

        actor.Tell(new ConfirmPlanned(userId, added.Plan.PlannedId, ActualAmount: 95m));
        ExpectMsg<PlannedConfirmed>();

        actor.Tell(new ListPlanned(userId));
        // Confirmed уже не показывается в активном списке.
        ExpectMsg<PlannedList>().Plans.Should().BeEmpty();
    }

    [Fact]
    public void Remove_planned_marks_cancelled()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserPlannedExpensesActor.CreateProps(userId));

        actor.Tell(new AddPlanned(userId, 100m, new DateOnly(2026, 6, 15), "x"));
        var added = ExpectMsg<PlannedAdded>();

        actor.Tell(new RemovePlanned(userId, added.Plan.PlannedId));
        ExpectMsg<PlannedRemoved>();

        actor.Tell(new ListPlanned(userId));
        ExpectMsg<PlannedList>().Plans.Should().BeEmpty();
    }

    [Fact]
    public void Confirm_already_confirmed_rejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserPlannedExpensesActor.CreateProps(userId));

        actor.Tell(new AddPlanned(userId, 100m, new DateOnly(2026, 6, 15), "x"));
        var added = ExpectMsg<PlannedAdded>();

        actor.Tell(new ConfirmPlanned(userId, added.Plan.PlannedId, null));
        ExpectMsg<PlannedConfirmed>();

        actor.Tell(new ConfirmPlanned(userId, added.Plan.PlannedId, null));
        ExpectMsg<PlannedRejected>();
    }
}
