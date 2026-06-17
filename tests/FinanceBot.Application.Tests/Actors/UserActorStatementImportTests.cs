using Akka.Actor;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class UserActorStatementImportTests : AkkaPersistenceTestBase
{
    private static ImportedTransaction Expense(decimal amount, string desc, int day = 5)
        => new(new DateOnly(2026, 6, day), amount, desc, TransactionKind.Expense);

    private static ImportedTransaction Income(decimal amount, string desc, int day = 1)
        => new(new DateOnly(2026, 6, day), amount, desc, TransactionKind.Income);

    private IActorRef RegisteredUserWithPeriod(Guid userId)
    {
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();
        actor.Tell(new ReportIncome(userId, 100_000m, DateTimeOffset.UtcNow, "зарплата"));
        ExpectMsg<IncomeAccepted>();
        return actor;
    }

    [Fact]
    public void Propose_replies_StatementImportProposed_with_counts_and_totals()
    {
        var userId = Guid.NewGuid();
        var actor = RegisteredUserWithPeriod(userId);

        var proposalId = Guid.NewGuid();
        actor.Tell(new ProposeStatementImport(userId, proposalId, new[]
        {
            Expense(750m, "обед"),
            Expense(1200m, "такси"),
            Income(5000m, "возврат")
        }));

        var proposed = ExpectMsg<StatementImportProposed>();
        proposed.ProposalId.Should().Be(proposalId);
        proposed.Count.Should().Be(3);
        proposed.ExpenseCount.Should().Be(2);
        proposed.IncomeCount.Should().Be(1);
        proposed.ExpenseTotal.Should().Be(1950m);
        proposed.IncomeTotal.Should().Be(5000m);
    }

    [Fact]
    public void Confirm_imports_expenses_and_incomes_and_replies_completed()
    {
        var userId = Guid.NewGuid();
        var actor = RegisteredUserWithPeriod(userId);

        var proposalId = Guid.NewGuid();
        actor.Tell(new ProposeStatementImport(userId, proposalId, new[]
        {
            Expense(750m, "обед"),
            Expense(1200m, "такси"),
            Income(5000m, "возврат")
        }));
        ExpectMsg<StatementImportProposed>();

        actor.Tell(new ConfirmStatementImport(userId, proposalId));

        var completed = ExpectMsg<StatementImportCompleted>();
        completed.Imported.Should().Be(3);
        completed.SkippedDuplicates.Should().Be(0);
        completed.Failed.Should().Be(0);
        completed.ExpenseTotal.Should().Be(1950m);
        completed.IncomeTotal.Should().Be(5000m);
    }

    [Fact]
    public void Confirm_dedups_identical_lines_within_batch()
    {
        var userId = Guid.NewGuid();
        var actor = RegisteredUserWithPeriod(userId);

        var proposalId = Guid.NewGuid();
        actor.Tell(new ProposeStatementImport(userId, proposalId, new[]
        {
            Expense(750m, "обед"),
            Expense(750m, "обед"), // дубль
            Expense(1200m, "такси")
        }));
        ExpectMsg<StatementImportProposed>();

        actor.Tell(new ConfirmStatementImport(userId, proposalId));

        var completed = ExpectMsg<StatementImportCompleted>();
        completed.Imported.Should().Be(2);
        completed.SkippedDuplicates.Should().Be(1);
    }

    [Fact]
    public void Confirm_with_wrong_proposalId_replies_rejected()
    {
        var userId = Guid.NewGuid();
        var actor = RegisteredUserWithPeriod(userId);

        actor.Tell(new ProposeStatementImport(userId, Guid.NewGuid(), new[] { Expense(750m, "обед") }));
        ExpectMsg<StatementImportProposed>();

        actor.Tell(new ConfirmStatementImport(userId, Guid.NewGuid()));

        ExpectMsg<StatementImportRejected>().Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Confirm_without_active_period_replies_rejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));
        actor.Tell(new RegisterUser(userId, 1L, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        var proposalId = Guid.NewGuid();
        actor.Tell(new ProposeStatementImport(userId, proposalId, new[] { Expense(750m, "обед") }));
        ExpectMsg<StatementImportProposed>();

        actor.Tell(new ConfirmStatementImport(userId, proposalId));

        ExpectMsg<StatementImportRejected>().Reason.Should().Contain("период");
    }

    [Fact]
    public void Cancel_clears_pending_so_confirm_is_rejected()
    {
        var userId = Guid.NewGuid();
        var actor = RegisteredUserWithPeriod(userId);

        var proposalId = Guid.NewGuid();
        actor.Tell(new ProposeStatementImport(userId, proposalId, new[] { Expense(750m, "обед") }));
        ExpectMsg<StatementImportProposed>();

        actor.Tell(new CancelStatementImport(userId));
        ExpectMsg<StatementImportCancelled>();

        actor.Tell(new ConfirmStatementImport(userId, proposalId));
        ExpectMsg<StatementImportRejected>();
    }

    [Fact]
    public void GetPending_returns_proposed_transactions()
    {
        var userId = Guid.NewGuid();
        var actor = RegisteredUserWithPeriod(userId);

        var proposalId = Guid.NewGuid();
        actor.Tell(new ProposeStatementImport(userId, proposalId, new[]
        {
            Expense(750m, "обед"),
            Income(5000m, "возврат")
        }));
        ExpectMsg<StatementImportProposed>();

        actor.Tell(new GetPendingStatementImport(userId));
        var list = ExpectMsg<StatementImportList>();
        list.ProposalId.Should().Be(proposalId);
        list.Transactions.Should().HaveCount(2);
    }
}
