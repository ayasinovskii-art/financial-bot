using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Telegram;

public sealed class TelegramRepliesTests
{
    private static ExpenseAccepted Accepted(
        Bucket bucket,
        decimal amount,
        decimal spentEssentials = 0m,
        decimal spentFun = 0m,
        decimal spentDeposit = 0m,
        decimal allocationEssentials = 25000m,
        decimal allocationFun = 12500m,
        decimal allocationDeposit = 12500m)
        => new(
            UserId: Guid.NewGuid(),
            ExpenseId: Guid.NewGuid(),
            PeriodId: Guid.NewGuid(),
            Amount: amount,
            Category: Category.Groceries,
            Bucket: bucket,
            SpentEssentials: spentEssentials,
            SpentFun: spentFun,
            SpentDeposit: spentDeposit,
            AllocationEssentials: allocationEssentials,
            AllocationFun: allocationFun,
            AllocationDeposit: allocationDeposit);

    [Fact]
    public void ExpenseAccepted_within_budget_has_no_overspend_warning()
    {
        var reply = TelegramReplies.ExpenseAccepted(Accepted(Bucket.Essentials, 750m, spentEssentials: 750m));

        reply.Should().Contain("осталось 24250.00 ₽");
        reply.Should().NotContain("⚠️");
        reply.Should().NotContain("Перерасход");
    }

    [Fact]
    public void ExpenseAccepted_exactly_zero_remaining_has_no_warning()
    {
        var reply = TelegramReplies.ExpenseAccepted(Accepted(Bucket.Essentials, 25000m, spentEssentials: 25000m));

        reply.Should().Contain("осталось 0.00 ₽");
        reply.Should().NotContain("⚠️");
    }

    [Fact]
    public void ExpenseAccepted_essentials_overspend_appends_warning()
    {
        var reply = TelegramReplies.ExpenseAccepted(Accepted(Bucket.Essentials, 1000m, spentEssentials: 26000m));

        reply.Should().Contain("⚠️ Перерасход бакета Essentials на 1000.00 ₽");
    }

    [Fact]
    public void ExpenseAccepted_fun_overspend_appends_warning()
    {
        var reply = TelegramReplies.ExpenseAccepted(Accepted(Bucket.Fun, 500m, spentFun: 12750.50m));

        reply.Should().Contain("⚠️ Перерасход бакета Fun на 250.50 ₽");
    }

    [Fact]
    public void ExpenseAccepted_deposit_overspend_appends_warning()
    {
        var reply = TelegramReplies.ExpenseAccepted(Accepted(Bucket.Deposit, 13000m, spentDeposit: 13000m));

        reply.Should().Contain("⚠️ Перерасход бакета Deposit на 500.00 ₽");
    }

    [Fact]
    public void ExpenseAccepted_deposit_within_budget_has_no_warning()
    {
        var reply = TelegramReplies.ExpenseAccepted(Accepted(Bucket.Deposit, 1000m, spentDeposit: 1000m));

        reply.Should().NotContain("⚠️");
    }
}
