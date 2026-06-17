using Akka.Actor;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class UserActorTests : AkkaPersistenceTestBase
{
    [Fact]
    public void Register_persists_and_replies_completed()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 12345, "UTC"));
        var reply = ExpectMsg<UserRegistrationCompleted>();
        reply.UserId.Should().Be(userId);
        reply.TelegramId.Should().Be(12345);
    }

    [Fact]
    public void Double_register_replies_AlreadyRegistered()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserAlreadyRegistered>();
    }

    [Fact]
    public void UpdateSettings_validates_value()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new UpdateSettings(userId, SettingsKey.SilenceDeadlineHours, "999"));
        var failed = ExpectMsg<SettingsValidationFailed>();
        failed.Key.Should().Be(SettingsKey.SilenceDeadlineHours);
    }

    [Fact]
    public void UpdateSettings_accepts_valid()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new UpdateSettings(userId, SettingsKey.SilenceDeadlineHours, "8"));
        var ok = ExpectMsg<SettingsUpdated>();
        ok.NewValue.Should().Be("8");

        actor.Tell(new GetUserSnapshot(userId));
        var snap = ExpectMsg<UserSnapshot>();
        snap.Settings["silence_deadline_hours"].Should().Be("8");
    }

    [Fact]
    public void ResetSettings_with_specific_key_clears_it()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new UpdateSettings(userId, SettingsKey.EveningTime, "20:00"));
        ExpectMsg<SettingsUpdated>();

        actor.Tell(new ResetSettings(userId, SettingsKey.EveningTime));
        ExpectMsg<SettingsResetCompleted>();

        actor.Tell(new GetUserSnapshot(userId));
        var snap = ExpectMsg<UserSnapshot>();
        snap.Settings.ContainsKey("evening_time").Should().BeFalse();
    }

    [Fact]
    public void ReportIncome_starts_period_and_allocates()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new ReportIncome(userId, 100_000m, DateTimeOffset.UtcNow, "salary"));
        var accepted = ExpectMsg<IncomeAccepted>();

        accepted.TotalIncome.Should().Be(100_000m);
        accepted.AllocationEssentials.Should().Be(50_000m);
        accepted.AllocationFun.Should().Be(25_000m);
        accepted.AllocationDeposit.Should().Be(25_000m);
    }

    [Fact]
    public void ReportIncome_rejects_zero_amount()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new ReportIncome(userId, 0m, DateTimeOffset.UtcNow, null));
        ExpectMsg<IncomeRejected>();
    }

    [Fact]
    public void Multiple_incomes_in_same_period_accumulate()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new ReportIncome(userId, 100_000m, DateTimeOffset.UtcNow, null));
        ExpectMsg<IncomeAccepted>();

        actor.Tell(new ReportIncome(userId, 50_000m, DateTimeOffset.UtcNow, null));
        var second = ExpectMsg<IncomeAccepted>();
        second.TotalIncome.Should().Be(150_000m);
        second.AllocationEssentials.Should().Be(75_000m);
    }

    [Fact]
    public void ReportExpense_without_period_rejects()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new ReportExpense(userId, 750m, DateTimeOffset.UtcNow, "обед", ExpenseSource.Manual));
        var rejected = ExpectMsg<ExpenseRejected>();
        rejected.Reason.Should().Contain("период");
    }

    [Fact]
    public void Cancel_returns_acknowledgement()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new Cancel(userId));
        ExpectMsg<CancelAcknowledged>();
    }

    [Fact]
    public void BulkAddExpenses_unregistered_replies_rejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        var rows = new List<BulkExpenseRow> { new(100m, new DateOnly(2026, 1, 1), "Кофе") };
        actor.Tell(new BulkAddExpenses(userId, Guid.Empty, rows));

        var rejected = ExpectMsg<BulkExpensesRejected>(TimeSpan.FromSeconds(3));
        rejected.UserId.Should().Be(userId);
        rejected.Reason.Should().Contain("зарегистрир");
    }

    [Fact]
    public void BulkAddExpenses_no_active_period_replies_rejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        var rows = new List<BulkExpenseRow> { new(100m, new DateOnly(2026, 1, 1), "Кофе") };
        actor.Tell(new BulkAddExpenses(userId, Guid.Empty, rows));

        var rejected = ExpectMsg<BulkExpensesRejected>(TimeSpan.FromSeconds(3));
        rejected.UserId.Should().Be(userId);
        rejected.Reason.Should().Contain("период");
    }

    [Fact]
    public void BulkAddExpenses_ten_rows_two_state_dups_adds_eight()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();
        actor.Tell(new ReportIncome(userId, 100_000m, DateTimeOffset.UtcNow, "salary"));
        ExpectMsg<IncomeAccepted>(TimeSpan.FromSeconds(3));

        // Seed 2 expenses into actor state (these become the state-level duplicates).
        var dupDate = new DateOnly(2026, 1, 10);
        var firstBatch = new List<BulkExpenseRow>
        {
            new(500m, dupDate, "Супермаркет"),
            new(300m, dupDate, "Кофе"),
        };
        actor.Tell(new BulkAddExpenses(userId, Guid.Empty, firstBatch));
        var first = ExpectMsg<BulkExpensesResult>(TimeSpan.FromSeconds(5));
        first.Added.Should().Be(2);
        first.Skipped.Should().Be(0);

        // 10 rows: 2 duplicate existing + 8 new.
        var tenRows = new List<BulkExpenseRow>
        {
            new(500m, dupDate, "Супермаркет"),   // dup
            new(300m, dupDate, "Кофе"),           // dup
            new(100m, new DateOnly(2026, 1, 11), "Аптека"),
            new(200m, new DateOnly(2026, 1, 11), "Транспорт"),
            new(150m, new DateOnly(2026, 1, 12), "Кино"),
            new(400m, new DateOnly(2026, 1, 12), "Ресторан"),
            new(250m, new DateOnly(2026, 1, 13), "Одежда"),
            new(350m, new DateOnly(2026, 1, 13), "Техника"),
            new(80m,  new DateOnly(2026, 1, 14), "Книги"),
            new(120m, new DateOnly(2026, 1, 14), "Подписки"),
        };
        actor.Tell(new BulkAddExpenses(userId, Guid.Empty, tenRows));
        var second = ExpectMsg<BulkExpensesResult>(TimeSpan.FromSeconds(5));
        second.Added.Should().Be(8);
        second.Skipped.Should().Be(2);
    }
}
