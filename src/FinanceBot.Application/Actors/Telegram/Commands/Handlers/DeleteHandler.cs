using System.Globalization;
using Akka.Actor;
using Akka.Hosting;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class DeleteHandler : ITelegramCommandHandler
{
    internal const int PageSizePublic = 5;
    private const int PageSize = PageSizePublic;
    private readonly IDeleteListReader _reader;

    public DeleteHandler(IDeleteListReader reader) => _reader = reader;

    public TelegramCommandKind Kind => TelegramCommandKind.Delete;

    public void Execute(TelegramCommandContext ctx)
    {
        var sub = ctx.ArgumentLine.Trim().ToLowerInvariant();
        switch (sub)
        {
            case "expense":
                FetchAndShowExpenses(ctx, skip: 0);
                break;
            case "income":
                FetchAndShowIncomes(ctx, skip: 0);
                break;
            case "goal":
                FetchAndShowGoals(ctx);
                break;
            case "":
                FetchAndShowAll(ctx);
                break;
            default:
                ctx.Reply(TelegramReplies.DeleteUsage());
                break;
        }
    }

    private void FetchAndShowExpenses(TelegramCommandContext ctx, int skip)
    {
        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var chatId = ctx.Update.ChatId;
        var self = ctx.Self;

        _reader.GetLastExpensesAsync(userId, skip, PageSize, CancellationToken.None)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully)
                {
                    return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, "Внутренняя ошибка.")]);
                }
                var rows = t.Result;
                if (rows.Count == 0)
                {
                    return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, TelegramReplies.DeleteEmpty("expense"))]);
                }
                var outgoing = new List<object>
                {
                    BuildExpenseKeyboard(chatId, rows, skip)
                };
                return new TelegramCommandCompleted(outgoing);
            })
            .PipeTo(self);
    }

    private void FetchAndShowIncomes(TelegramCommandContext ctx, int skip)
    {
        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var chatId = ctx.Update.ChatId;
        var self = ctx.Self;

        _reader.GetLastIncomesAsync(userId, skip, PageSize, CancellationToken.None)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully)
                {
                    return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, "Внутренняя ошибка.")]);
                }
                var rows = t.Result;
                if (rows.Count == 0)
                {
                    return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, TelegramReplies.DeleteEmpty("income"))]);
                }
                var outgoing = new List<object>
                {
                    BuildIncomeKeyboard(chatId, rows, skip)
                };
                return new TelegramCommandCompleted(outgoing);
            })
            .PipeTo(self);
    }

    private static void FetchAndShowGoals(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var chatId = ctx.Update.ChatId;
        var self = ctx.Self;
        var timeout = ctx.AskTimeout;

        shard.Ask<object>(new ShardEnvelope(userId.ToString("N"), new GetUserGoals(userId)), timeout)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully || t.Result is not UserGoalsList goalsList)
                {
                    return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, "Внутренняя ошибка.")]);
                }
                var active = goalsList.Goals.Where(g => !g.IsCompleted).ToList();
                if (active.Count == 0)
                {
                    return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, TelegramReplies.DeleteEmpty("goal"))]);
                }
                return new TelegramCommandCompleted([BuildGoalKeyboard(chatId, active)]);
            })
            .PipeTo(self);
    }

    private void FetchAndShowAll(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var chatId = ctx.Update.ChatId;
        var self = ctx.Self;
        var timeout = ctx.AskTimeout;
        var reader = _reader;

        Task.Run(async () =>
        {
            var expensesTask = reader.GetLastExpensesAsync(userId, 0, PageSize, CancellationToken.None);
            var incomesTask = reader.GetLastIncomesAsync(userId, 0, PageSize, CancellationToken.None);
            var goalsTask = shard.Ask<object>(
                new ShardEnvelope(userId.ToString("N"), new GetUserGoals(userId)), timeout);

            await Task.WhenAll(expensesTask, incomesTask, goalsTask).ConfigureAwait(false);

            var outgoing = new List<object>();

            var expenses = expensesTask.Result;
            if (expenses.Count > 0)
            {
                outgoing.Add(BuildExpenseKeyboard(chatId, expenses, 0));
            }

            var incomes = incomesTask.Result;
            if (incomes.Count > 0)
            {
                outgoing.Add(BuildIncomeKeyboard(chatId, incomes, 0));
            }

            if (goalsTask.Result is UserGoalsList goalsList)
            {
                var active = goalsList.Goals.Where(g => !g.IsCompleted).ToList();
                if (active.Count > 0)
                {
                    outgoing.Add(BuildGoalKeyboard(chatId, active));
                }
            }

            if (outgoing.Count == 0)
            {
                outgoing.Add(new OutgoingTelegramReply(chatId, TelegramReplies.DeleteEmpty("all")));
            }

            return new TelegramCommandCompleted(outgoing);
        }).PipeTo(self);
    }

    internal static OutgoingInlineKeyboard BuildExpenseKeyboard(
        long chatId, IReadOnlyList<DeleteExpenseRow> rows, int skip)
    {
        var rowButtons = new List<IReadOnlyList<InlineButton>>();
        foreach (var r in rows)
        {
            var label = $"{r.OccurredAt:dd.MM} {r.Description} {r.Amount.ToString("0", CultureInfo.InvariantCulture)}₽";
            var deleteBtn = new InlineButton("❌", CallbackPayload.Encode("del", r.Id, "e"));
            rowButtons.Add([new InlineButton(label, CallbackPayload.Encode("del", r.Id, "e")), deleteBtn]);
        }
        if (rows.Count == PageSize)
        {
            var moreBtn = new InlineButton("Показать ещё", CallbackPayload.Encode("delm", Guid.Empty, $"e:{skip + PageSize}"));
            rowButtons.Add([moreBtn]);
        }
        return new OutgoingInlineKeyboard(chatId, TelegramReplies.DeleteListHeader("expense"), rowButtons);
    }

    internal static OutgoingInlineKeyboard BuildIncomeKeyboard(
        long chatId, IReadOnlyList<DeleteIncomeRow> rows, int skip)
    {
        var rowButtons = new List<IReadOnlyList<InlineButton>>();
        foreach (var r in rows)
        {
            var desc = string.IsNullOrWhiteSpace(r.Description) ? "" : $" {r.Description}";
            var label = $"{r.OccurredAt:dd.MM}{desc} {r.Amount.ToString("0", CultureInfo.InvariantCulture)}₽";
            var deleteBtn = new InlineButton("❌", CallbackPayload.Encode("del", r.Id, "i"));
            rowButtons.Add([new InlineButton(label, CallbackPayload.Encode("del", r.Id, "i")), deleteBtn]);
        }
        if (rows.Count == PageSize)
        {
            var moreBtn = new InlineButton("Показать ещё", CallbackPayload.Encode("delm", Guid.Empty, $"i:{skip + PageSize}"));
            rowButtons.Add([moreBtn]);
        }
        return new OutgoingInlineKeyboard(chatId, TelegramReplies.DeleteListHeader("income"), rowButtons);
    }

    internal static OutgoingInlineKeyboard BuildGoalKeyboard(long chatId, IReadOnlyList<GoalState> goals)
    {
        var rowButtons = new List<IReadOnlyList<InlineButton>>();
        foreach (var g in goals)
        {
            var deleteBtn = new InlineButton("❌", CallbackPayload.Encode("del", g.GoalId, "g"));
            rowButtons.Add([new InlineButton(g.Description, CallbackPayload.Encode("del", g.GoalId, "g")), deleteBtn]);
        }
        return new OutgoingInlineKeyboard(chatId, TelegramReplies.DeleteListHeader("goal"), rowButtons);
    }
}

public sealed class DeleteCallbackHandler : ITelegramCallbackHandler
{
    private readonly IDeleteListReader _reader;

    public DeleteCallbackHandler(IDeleteListReader reader) => _reader = reader;

    public string DataPrefix => "del:";

    public void Execute(TelegramCallbackContext ctx)
    {
        if (!CallbackPayload.TryDecode(ctx.Callback.Data, out _, out var entityId, out var typeChar)
            || typeChar is null)
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Не понял callback."));
            return;
        }

        var chatId = ctx.Callback.ChatId;
        var callbackId = ctx.Callback.CallbackQueryId;
        var telegramId = ctx.Callback.TelegramId;
        var self = ctx.Self;
        var timeout = ctx.AskTimeout;
        var system = ctx.System;
        var reader = _reader;
        var callbackUserId = UserIdFromTelegramId.Resolve(telegramId);

        switch (typeChar)
        {
            case "e":
                reader.GetExpenseAsync(callbackUserId, entityId, CancellationToken.None)
                    .ContinueWith(t =>
                    {
                        var outgoing = new List<object> { new OutgoingCallbackAck(callbackId, null) };
                        if (!t.IsCompletedSuccessfully || t.Result is null)
                        {
                            outgoing.Add(new OutgoingTelegramReply(chatId, "Запись не найдена."));
                            return new TelegramCommandCompleted(outgoing);
                        }
                        var row = t.Result;
                        var text = TelegramReplies.DeleteConfirmPrompt(row.Description, row.Amount, row.OccurredAt);
                        var yesBtn = new InlineButton("Да", CallbackPayload.Encode("delc", entityId, "ey"));
                        var noBtn = new InlineButton("Нет", CallbackPayload.Encode("delx", entityId, "n"));
                        outgoing.Add(new OutgoingInlineKeyboard(chatId, text, [[yesBtn, noBtn]]));
                        return new TelegramCommandCompleted(outgoing);
                    })
                    .PipeTo(self);
                break;

            case "i":
                reader.GetIncomeAsync(callbackUserId, entityId, CancellationToken.None)
                    .ContinueWith(t =>
                    {
                        var outgoing = new List<object> { new OutgoingCallbackAck(callbackId, null) };
                        if (!t.IsCompletedSuccessfully || t.Result is null)
                        {
                            outgoing.Add(new OutgoingTelegramReply(chatId, "Запись не найдена."));
                            return new TelegramCommandCompleted(outgoing);
                        }
                        var row = t.Result;
                        var desc = row.Description ?? "";
                        var text = TelegramReplies.DeleteConfirmPrompt(desc, row.Amount, row.OccurredAt);
                        var yesBtn = new InlineButton("Да", CallbackPayload.Encode("delc", entityId, "iy"));
                        var noBtn = new InlineButton("Нет", CallbackPayload.Encode("delx", entityId, "n"));
                        outgoing.Add(new OutgoingInlineKeyboard(chatId, text, [[yesBtn, noBtn]]));
                        return new TelegramCommandCompleted(outgoing);
                    })
                    .PipeTo(self);
                break;

            case "g":
                var registry = ActorRegistry.For(system);
                if (!registry.TryGet<UserShardMarker>(out var shard))
                {
                    self.Tell(new OutgoingCallbackAck(callbackId, "Внутренняя ошибка."));
                    return;
                }
                var userId = UserIdFromTelegramId.Resolve(telegramId);
                shard.Ask<object>(new ShardEnvelope(userId.ToString("N"), new GetUserGoals(userId)), timeout)
                    .ContinueWith(t =>
                    {
                        var outgoing = new List<object> { new OutgoingCallbackAck(callbackId, null) };
                        if (!t.IsCompletedSuccessfully || t.Result is not UserGoalsList gl)
                        {
                            outgoing.Add(new OutgoingTelegramReply(chatId, "Внутренняя ошибка."));
                            return new TelegramCommandCompleted(outgoing);
                        }
                        var goal = gl.Goals.FirstOrDefault(g => g.GoalId == entityId);
                        if (goal is null)
                        {
                            outgoing.Add(new OutgoingTelegramReply(chatId, "Цель не найдена."));
                            return new TelegramCommandCompleted(outgoing);
                        }
                        var text = TelegramReplies.DeleteConfirmGoalPrompt(goal.Description);
                        var yesBtn = new InlineButton("Да", CallbackPayload.Encode("delc", entityId, "gy"));
                        var noBtn = new InlineButton("Нет", CallbackPayload.Encode("delx", entityId, "n"));
                        outgoing.Add(new OutgoingInlineKeyboard(chatId, text, [[yesBtn, noBtn]]));
                        return new TelegramCommandCompleted(outgoing);
                    })
                    .PipeTo(self);
                break;

            default:
                self.Tell(new OutgoingCallbackAck(callbackId, "Не понял тип записи."));
                break;
        }
    }
}

public sealed class DeleteMoreCallbackHandler : ITelegramCallbackHandler
{
    private readonly IDeleteListReader _reader;

    public DeleteMoreCallbackHandler(IDeleteListReader reader) => _reader = reader;

    public string DataPrefix => "delm:";

    public void Execute(TelegramCallbackContext ctx)
    {
        if (!CallbackPayload.TryDecode(ctx.Callback.Data, out _, out _, out var shortArg)
            || shortArg is null)
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Не понял callback."));
            return;
        }

        var parts = shortArg.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var skip))
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Не понял callback."));
            return;
        }

        var typeChar = parts[0];
        var chatId = ctx.Callback.ChatId;
        var callbackId = ctx.Callback.CallbackQueryId;
        var telegramId = ctx.Callback.TelegramId;
        var self = ctx.Self;
        var timeout = ctx.AskTimeout;
        var system = ctx.System;
        var reader = _reader;
        var userId = UserIdFromTelegramId.Resolve(telegramId);

        switch (typeChar)
        {
            case "e":
                reader.GetLastExpensesAsync(userId, skip, DeleteHandler.PageSizePublic, CancellationToken.None)
                    .ContinueWith(t =>
                    {
                        var outgoing = new List<object> { new OutgoingCallbackAck(callbackId, null) };
                        if (!t.IsCompletedSuccessfully)
                        {
                            outgoing.Add(new OutgoingTelegramReply(chatId, "Внутренняя ошибка."));
                            return new TelegramCommandCompleted(outgoing);
                        }
                        var rows = t.Result;
                        if (rows.Count == 0)
                        {
                            outgoing.Add(new OutgoingTelegramReply(chatId, TelegramReplies.DeleteEmpty("expense")));
                        }
                        else
                        {
                            outgoing.Add(DeleteHandler.BuildExpenseKeyboard(chatId, rows, skip));
                        }
                        return new TelegramCommandCompleted(outgoing);
                    })
                    .PipeTo(self);
                break;

            case "i":
                reader.GetLastIncomesAsync(userId, skip, DeleteHandler.PageSizePublic, CancellationToken.None)
                    .ContinueWith(t =>
                    {
                        var outgoing = new List<object> { new OutgoingCallbackAck(callbackId, null) };
                        if (!t.IsCompletedSuccessfully)
                        {
                            outgoing.Add(new OutgoingTelegramReply(chatId, "Внутренняя ошибка."));
                            return new TelegramCommandCompleted(outgoing);
                        }
                        var rows = t.Result;
                        if (rows.Count == 0)
                        {
                            outgoing.Add(new OutgoingTelegramReply(chatId, TelegramReplies.DeleteEmpty("income")));
                        }
                        else
                        {
                            outgoing.Add(DeleteHandler.BuildIncomeKeyboard(chatId, rows, skip));
                        }
                        return new TelegramCommandCompleted(outgoing);
                    })
                    .PipeTo(self);
                break;

            case "g":
                var registry = ActorRegistry.For(system);
                if (!registry.TryGet<UserShardMarker>(out var shard))
                {
                    self.Tell(new OutgoingCallbackAck(callbackId, "Внутренняя ошибка."));
                    return;
                }
                shard.Ask<object>(
                    new ShardEnvelope(userId.ToString("N"), new GetUserGoals(userId)), timeout)
                    .ContinueWith(t =>
                    {
                        var outgoing = new List<object> { new OutgoingCallbackAck(callbackId, null) };
                        if (!t.IsCompletedSuccessfully || t.Result is not UserGoalsList goalsList)
                        {
                            outgoing.Add(new OutgoingTelegramReply(chatId, "Внутренняя ошибка."));
                            return new TelegramCommandCompleted(outgoing);
                        }
                        var active = goalsList.Goals.Where(g => !g.IsCompleted).ToList();
                        if (active.Count == 0)
                        {
                            outgoing.Add(new OutgoingTelegramReply(chatId, TelegramReplies.DeleteEmpty("goal")));
                        }
                        else
                        {
                            outgoing.Add(DeleteHandler.BuildGoalKeyboard(chatId, active));
                        }
                        return new TelegramCommandCompleted(outgoing);
                    })
                    .PipeTo(self);
                break;

            default:
                self.Tell(new OutgoingCallbackAck(callbackId, "Не понял тип записи."));
                break;
        }
    }
}

public sealed class DeleteConfirmCallbackHandler : ITelegramCallbackHandler
{
    public string DataPrefix => "delc:";

    public void Execute(TelegramCallbackContext ctx)
    {
        if (!CallbackPayload.TryDecode(ctx.Callback.Data, out _, out var entityId, out var shortArg)
            || shortArg is null || shortArg.Length < 2)
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Не понял callback."));
            return;
        }

        var typeChar = shortArg[0].ToString();
        var chatId = ctx.Callback.ChatId;
        var callbackId = ctx.Callback.CallbackQueryId;
        var telegramId = ctx.Callback.TelegramId;
        var self = ctx.Self;
        var timeout = ctx.AskTimeout;
        var system = ctx.System;

        var registry = ActorRegistry.For(system);
        if (!registry.TryGet<UserShardMarker>(out var shard))
        {
            self.Tell(new OutgoingCallbackAck(callbackId, "Внутренняя ошибка."));
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(telegramId);

        object command = typeChar switch
        {
            "e" => new ShardEnvelope(userId.ToString("N"), new DeleteExpense(userId, entityId, "user-delete")),
            "i" => new ShardEnvelope(userId.ToString("N"), new DeleteIncome(userId, entityId)),
            "g" => new ShardEnvelope(userId.ToString("N"), new RemoveGoal(userId, entityId)),
            _ => null!
        };

        if (command is null)
        {
            self.Tell(new OutgoingCallbackAck(callbackId, "Не понял тип записи."));
            return;
        }

        shard.Ask<object>(command, timeout)
            .ContinueWith(t =>
            {
                var outgoing = new List<object> { new OutgoingCallbackAck(callbackId, null) };
                if (!t.IsCompletedSuccessfully)
                {
                    outgoing.Add(new OutgoingTelegramReply(chatId, "Внутренняя ошибка."));
                    return new TelegramCommandCompleted(outgoing);
                }
                var text = t.Result switch
                {
                    DeletedSuccessfully => TelegramReplies.DeleteDone(),
                    DeleteRejected r => r.Reason,
                    GoalRejected r => r.Reason,
                    _ => TelegramReplies.DeleteDone()
                };
                outgoing.Add(new OutgoingTelegramReply(chatId, text));
                return new TelegramCommandCompleted(outgoing);
            })
            .PipeTo(self);
    }
}

public sealed class DeleteCancelCallbackHandler : ITelegramCallbackHandler
{
    public string DataPrefix => "delx:";

    public void Execute(TelegramCallbackContext ctx)
    {
        ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, null));
        ctx.Self.Tell(new OutgoingTelegramReply(ctx.Callback.ChatId, TelegramReplies.DeleteCancelled()));
    }
}
