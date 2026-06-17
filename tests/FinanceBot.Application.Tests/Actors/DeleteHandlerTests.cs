using Akka.Event;
using Akka.TestKit.Xunit2;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.Telegram.Commands;
using FinanceBot.Application.Actors.Telegram.Commands.Handlers;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class DeleteHandlerTests : TestKit
{
    private const long ChatId = 9000L;

    [Fact]
    public void BuildExpenseKeyboard_fewer_than_page_size_has_no_show_more_button()
    {
        var rows = new List<DeleteExpenseRow>
        {
            new(Guid.NewGuid(), 700m, "Обед", DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), 350m, "Такси", DateTimeOffset.UtcNow),
        };

        var kb = DeleteHandler.BuildExpenseKeyboard(ChatId, rows, skip: 0);

        kb.Rows.Should().HaveCount(2);
        kb.Rows.Should().NotContain(r => r.Any(b => b.Text == "Показать ещё"));
    }

    [Fact]
    public void BuildExpenseKeyboard_exactly_page_size_rows_appends_show_more_button()
    {
        var rows = Enumerable.Range(0, 5)
            .Select(i => new DeleteExpenseRow(Guid.NewGuid(), 100m * (i + 1), $"Item{i}", DateTimeOffset.UtcNow))
            .ToList();

        var kb = DeleteHandler.BuildExpenseKeyboard(ChatId, rows, skip: 0);

        var lastRow = kb.Rows[^1];
        lastRow.Should().ContainSingle(b => b.Text == "Показать ещё");
    }

    [Fact]
    public void BuildIncomeKeyboard_null_description_label_contains_ruble_and_callback_prefix_is_del()
    {
        var id = Guid.NewGuid();
        var rows = new List<DeleteIncomeRow>
        {
            new(id, 50_000m, null, DateTimeOffset.UtcNow),
        };

        var kb = DeleteHandler.BuildIncomeKeyboard(ChatId, rows, skip: 0);

        kb.Rows.Should().HaveCount(1);
        var labelBtn = kb.Rows[0][0];
        labelBtn.Text.Should().Contain("₽");
        CallbackPayload.TryDecode(labelBtn.CallbackData, out var action, out _, out _).Should().BeTrue();
        action.Should().Be("del");
    }

    [Fact]
    public void BuildGoalKeyboard_single_goal_first_button_label_equals_description()
    {
        var goalId = Guid.NewGuid();
        var goal = new GoalState(goalId, "Купить машину", null, null, IsCompleted: false);

        var kb = DeleteHandler.BuildGoalKeyboard(ChatId, [goal]);

        kb.Rows.Should().HaveCount(1);
        kb.Rows[0][0].Text.Should().Be("Купить машину");
    }

    [Fact]
    public void DeleteCancelCallbackHandler_execute_sends_ack_then_cancelled_reply()
    {
        var callback = new IncomingCallbackQuery(
            UpdateId: 1, CallbackQueryId: "cq-cancel", ChatId: ChatId, TelegramId: 42L,
            Username: null, FirstName: null,
            Data: CallbackPayload.Encode("delx", Guid.NewGuid(), "n"),
            SentAt: DateTimeOffset.UtcNow);

        var ctx = new TelegramCallbackContext
        {
            Callback = callback,
            Self = TestActor,
            System = Sys,
            Log = Logging.GetLogger(Sys, nameof(DeleteHandlerTests)),
            AskTimeout = TimeSpan.FromSeconds(3),
        };

        new DeleteCancelCallbackHandler().Execute(ctx);

        ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(2));
        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(2));
        reply.Text.Should().Be(TelegramReplies.DeleteCancelled());
    }
}
