using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Xunit2;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.Telegram.Commands;
using FinanceBot.Application.Actors.Telegram.Commands.Handlers;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class DeleteHandlerTests : TestKit
{
    private const long ChatId = 9000L;
    private const long TelegramId = 42L;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private TelegramCommandContext MakeCommandCtx(string argumentLine, IActorRef? shardRef = null)
    {
        if (shardRef != null)
            Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(shardRef);

        return new TelegramCommandContext
        {
            Update = new IncomingTelegramUpdate(1L, ChatId, TelegramId, null, "Test", null,
                "/delete " + argumentLine, DateTimeOffset.UtcNow),
            ArgumentLine = argumentLine,
            Allowed = new AccessDecision.Allowed(TelegramId, AccessRole.User),
            Self = TestActor,
            System = Sys,
            Log = Logging.GetLogger(Sys, nameof(DeleteHandlerTests)),
            Defaults = new UserDefaultsOptions(),
            AskTimeout = TimeSpan.FromSeconds(5),
        };
    }

    private TelegramCallbackContext MakeCallbackCtx(string data)
    {
        return new TelegramCallbackContext
        {
            Callback = new IncomingCallbackQuery(1, "cq1", ChatId, TelegramId, null, null,
                data, DateTimeOffset.UtcNow),
            Self = TestActor,
            System = Sys,
            Log = Logging.GetLogger(Sys, nameof(DeleteHandlerTests)),
            AskTimeout = TimeSpan.FromSeconds(5),
        };
    }

    private sealed class FakeDeleteListReader : IDeleteListReader
    {
        public IReadOnlyList<DeleteExpenseRow> Expenses { get; set; } = [];
        public IReadOnlyList<DeleteIncomeRow> Incomes { get; set; } = [];
        public DeleteExpenseRow? SingleExpense { get; set; }
        public DeleteIncomeRow? SingleIncome { get; set; }

        public Task<IReadOnlyList<DeleteExpenseRow>> GetLastExpensesAsync(Guid userId, int skip, int take, CancellationToken ct)
            => Task.FromResult(Expenses);
        public Task<IReadOnlyList<DeleteIncomeRow>> GetLastIncomesAsync(Guid userId, int skip, int take, CancellationToken ct)
            => Task.FromResult(Incomes);
        public Task<DeleteExpenseRow?> GetExpenseAsync(Guid userId, Guid id, CancellationToken ct)
            => Task.FromResult(SingleExpense);
        public Task<DeleteIncomeRow?> GetIncomeAsync(Guid userId, Guid id, CancellationToken ct)
            => Task.FromResult(SingleIncome);
    }

    // ── BuildKeyboard helpers ─────────────────────────────────────────────────

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

    // ── DeleteHandler.Execute — all 5 arms ───────────────────────────────────

    [Fact]
    public void DeleteHandler_execute_expense_subcommand_returns_inline_keyboard()
    {
        var reader = new FakeDeleteListReader
        {
            Expenses =
            [
                new(Guid.NewGuid(), 700m, "Обед", DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), 350m, "Такси", DateTimeOffset.UtcNow),
            ]
        };
        var ctx = MakeCommandCtx("expense");

        new DeleteHandler(reader).Execute(ctx);

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        completed.Outgoing.OfType<OutgoingInlineKeyboard>().Should().ContainSingle();
    }

    [Fact]
    public void DeleteHandler_execute_income_subcommand_returns_inline_keyboard()
    {
        var reader = new FakeDeleteListReader
        {
            Incomes = [new(Guid.NewGuid(), 50_000m, "Зарплата", DateTimeOffset.UtcNow)]
        };
        var ctx = MakeCommandCtx("income");

        new DeleteHandler(reader).Execute(ctx);

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        completed.Outgoing.OfType<OutgoingInlineKeyboard>().Should().ContainSingle();
    }

    [Fact]
    public void DeleteHandler_execute_goal_subcommand_returns_inline_keyboard()
    {
        var shardProbe = CreateTestProbe();
        var goalId = Guid.NewGuid();
        var reader = new FakeDeleteListReader();
        var ctx = MakeCommandCtx("goal", shardProbe.Ref);

        new DeleteHandler(reader).Execute(ctx);

        var envelope = shardProbe.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(3));
        envelope.Message.Should().BeOfType<GetUserGoals>();
        shardProbe.Reply(new UserGoalsList([new GoalState(goalId, "Отпуск", null, null, IsCompleted: false)]));

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        completed.Outgoing.OfType<OutgoingInlineKeyboard>().Should().ContainSingle();
    }

    [Fact]
    public void DeleteHandler_execute_empty_subcommand_combines_all_types()
    {
        var shardProbe = CreateTestProbe();
        var goalId = Guid.NewGuid();
        var reader = new FakeDeleteListReader
        {
            Expenses = [new(Guid.NewGuid(), 100m, "Кофе", DateTimeOffset.UtcNow)],
            Incomes = [new(Guid.NewGuid(), 50_000m, null, DateTimeOffset.UtcNow)]
        };
        var ctx = MakeCommandCtx("", shardProbe.Ref);

        new DeleteHandler(reader).Execute(ctx);

        shardProbe.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(3));
        shardProbe.Reply(new UserGoalsList([new GoalState(goalId, "Отпуск", null, null, IsCompleted: false)]));

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        completed.Outgoing.OfType<OutgoingInlineKeyboard>().Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void DeleteHandler_execute_unknown_subcommand_replies_usage()
    {
        var reader = new FakeDeleteListReader();
        var ctx = MakeCommandCtx("xyz");

        new DeleteHandler(reader).Execute(ctx);

        var reply = ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(2));
        reply.ChatId.Should().Be(ChatId);
    }

    // ── DeleteCallbackHandler.Execute — typeChar arms ────────────────────────

    [Fact]
    public void DeleteCallbackHandler_execute_expense_found_returns_confirm_keyboard()
    {
        var expenseId = Guid.NewGuid();
        var reader = new FakeDeleteListReader
        {
            SingleExpense = new DeleteExpenseRow(expenseId, 700m, "Обед", DateTimeOffset.UtcNow)
        };
        var ctx = MakeCallbackCtx(CallbackPayload.Encode("del", expenseId, "e"));

        new DeleteCallbackHandler(reader).Execute(ctx);

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        var kb = completed.Outgoing.OfType<OutgoingInlineKeyboard>().Should().ContainSingle().Subject;
        kb.Rows.Should().ContainSingle(row =>
            row.Any(b => b.Text == "Да") && row.Any(b => b.Text == "Нет"));
    }

    [Fact]
    public void DeleteCallbackHandler_execute_expense_not_found_sends_error_reply()
    {
        var reader = new FakeDeleteListReader(); // SingleExpense is null
        var ctx = MakeCallbackCtx(CallbackPayload.Encode("del", Guid.NewGuid(), "e"));

        new DeleteCallbackHandler(reader).Execute(ctx);

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        completed.Outgoing.OfType<OutgoingTelegramReply>()
            .Should().ContainSingle(r => r.Text == "Запись не найдена.");
    }

    [Fact]
    public void DeleteCallbackHandler_execute_income_found_returns_confirm_keyboard()
    {
        var incomeId = Guid.NewGuid();
        var reader = new FakeDeleteListReader
        {
            SingleIncome = new DeleteIncomeRow(incomeId, 50_000m, "Зарплата", DateTimeOffset.UtcNow)
        };
        var ctx = MakeCallbackCtx(CallbackPayload.Encode("del", incomeId, "i"));

        new DeleteCallbackHandler(reader).Execute(ctx);

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        var kb = completed.Outgoing.OfType<OutgoingInlineKeyboard>().Should().ContainSingle().Subject;
        kb.Rows.Should().ContainSingle(row =>
            row.Any(b => b.Text == "Да") && row.Any(b => b.Text == "Нет"));
    }

    [Fact]
    public void DeleteCallbackHandler_execute_goal_found_returns_confirm_keyboard()
    {
        var goalId = Guid.NewGuid();
        var shardProbe = CreateTestProbe();
        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(shardProbe.Ref);

        var reader = new FakeDeleteListReader();
        var ctx = MakeCallbackCtx(CallbackPayload.Encode("del", goalId, "g"));

        new DeleteCallbackHandler(reader).Execute(ctx);

        var envelope = shardProbe.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(3));
        envelope.Message.Should().BeOfType<GetUserGoals>();
        shardProbe.Reply(new UserGoalsList([new GoalState(goalId, "Отпуск", null, null, IsCompleted: false)]));

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        var kb = completed.Outgoing.OfType<OutgoingInlineKeyboard>().Should().ContainSingle().Subject;
        kb.Rows.Should().ContainSingle(row =>
            row.Any(b => b.Text == "Да") && row.Any(b => b.Text == "Нет"));
    }

    [Fact]
    public void DeleteCallbackHandler_execute_unknown_type_char_sends_error_ack()
    {
        var reader = new FakeDeleteListReader();
        var ctx = MakeCallbackCtx(CallbackPayload.Encode("del", Guid.NewGuid(), "x"));

        new DeleteCallbackHandler(reader).Execute(ctx);

        var ack = ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(2));
        ack.Text.Should().NotBeNull();
    }

    // ── DeleteMoreCallbackHandler.Execute — typeChar arms ────────────────────

    [Fact]
    public void DeleteMoreCallbackHandler_execute_expense_returns_inline_keyboard()
    {
        var reader = new FakeDeleteListReader
        {
            Expenses =
            [
                new(Guid.NewGuid(), 100m, "Кофе", DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), 200m, "Обед", DateTimeOffset.UtcNow),
            ]
        };
        var ctx = MakeCallbackCtx(CallbackPayload.Encode("delm", Guid.Empty, "e:5"));

        new DeleteMoreCallbackHandler(reader).Execute(ctx);

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        completed.Outgoing.OfType<OutgoingInlineKeyboard>().Should().ContainSingle();
    }

    [Fact]
    public void DeleteMoreCallbackHandler_execute_income_returns_inline_keyboard()
    {
        var reader = new FakeDeleteListReader
        {
            Incomes = [new(Guid.NewGuid(), 50_000m, "Зарплата", DateTimeOffset.UtcNow)]
        };
        var ctx = MakeCallbackCtx(CallbackPayload.Encode("delm", Guid.Empty, "i:5"));

        new DeleteMoreCallbackHandler(reader).Execute(ctx);

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        completed.Outgoing.OfType<OutgoingInlineKeyboard>().Should().ContainSingle();
    }

    // ── DeleteConfirmCallbackHandler.Execute ─────────────────────────────────

    [Fact]
    public void DeleteConfirmCallbackHandler_no_registry_sends_error_ack()
    {
        var ctx = MakeCallbackCtx(CallbackPayload.Encode("delc", Guid.NewGuid(), "ey"));

        new DeleteConfirmCallbackHandler().Execute(ctx);

        var ack = ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(2));
        ack.Text.Should().NotBeNull();
    }

    [Fact]
    public void DeleteConfirmCallbackHandler_expense_sends_command_and_replies_done()
    {
        var shardProbe = CreateTestProbe();
        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(shardProbe.Ref);

        var expenseId = Guid.NewGuid();
        var ctx = MakeCallbackCtx(CallbackPayload.Encode("delc", expenseId, "ey"));

        new DeleteConfirmCallbackHandler().Execute(ctx);

        shardProbe.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(3));
        shardProbe.Reply(new DeletedSuccessfully());

        var completed = ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(5));
        completed.Outgoing.OfType<OutgoingTelegramReply>()
            .Should().ContainSingle(r => r.Text == TelegramReplies.DeleteDone());
    }

    // ── DeleteCancelCallbackHandler.Execute ──────────────────────────────────

    [Fact]
    public void DeleteCancelCallbackHandler_execute_sends_ack_then_cancelled_reply()
    {
        var callback = new IncomingCallbackQuery(
            UpdateId: 1, CallbackQueryId: "cq-cancel", ChatId: ChatId, TelegramId: TelegramId,
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
