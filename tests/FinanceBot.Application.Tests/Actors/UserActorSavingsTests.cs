using Akka.Actor;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

/// <summary>Stage 15: /savings и автоматическое закрытие периода при следующем доходе.</summary>
public sealed class UserActorSavingsTests : AkkaPersistenceTestBase
{
    [Fact]
    public void ConfirmSavings_active_period_succeeds()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();
        actor.Tell(new ReportIncome(userId, 100_000m, DateTimeOffset.UtcNow, null));
        ExpectMsg<IncomeAccepted>();

        actor.Tell(new ConfirmSavings(userId, Guid.Empty, 12000m));
        ExpectMsg<SavingsAccepted>().Amount.Should().Be(12000m);
    }

    [Fact]
    public void Income_after_savings_starts_new_period_and_closes_old()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();
        actor.Tell(new ReportIncome(userId, 50_000m, DateTimeOffset.UtcNow, null));
        var first = ExpectMsg<IncomeAccepted>();

        actor.Tell(new ConfirmSavings(userId, Guid.Empty, 5000m));
        ExpectMsg<SavingsAccepted>();

        actor.Tell(new ReportIncome(userId, 60_000m, DateTimeOffset.UtcNow, null));
        var second = ExpectMsg<IncomeAccepted>();

        // Это уже новый период — другая PeriodId.
        second.PeriodId.Should().NotBe(first.PeriodId);
        second.TotalIncome.Should().Be(60_000m);
    }

    [Fact]
    public void ConfirmSavings_without_active_period_rejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new ConfirmSavings(userId, Guid.Empty, 5000m));
        ExpectMsg<SavingsRejected>();
    }
}
