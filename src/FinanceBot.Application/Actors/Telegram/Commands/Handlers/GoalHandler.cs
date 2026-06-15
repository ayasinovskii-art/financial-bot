using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class GoalHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Goal;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var trimmed = ctx.ArgumentLine.Trim();
        var spaceIdx = trimmed.IndexOf(' ', StringComparison.Ordinal);
        var verb = spaceIdx < 0
            ? trimmed.ToLowerInvariant()
            : trimmed[..spaceIdx].ToLowerInvariant();
        var rest = spaceIdx < 0 ? string.Empty : trimmed[(spaceIdx + 1)..].Trim();

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);

        switch (verb)
        {
            case "add":
                HandleAdd(ctx, shard, userId, rest);
                break;
            case "list":
                HandleList(ctx, shard, userId);
                break;
            case "done":
                HandleDone(ctx, shard, userId, rest);
                break;
            default:
                ctx.Reply(TelegramReplies.GoalUsage());
                break;
        }
    }

    private static void HandleAdd(TelegramCommandContext ctx, IActorRef shard, Guid userId, string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            ctx.Reply("Укажи описание цели: `/goal add <описание>`.");
            return;
        }

        var cmd = new AddGoal(userId, Guid.NewGuid(), description, TargetAmount: null, TargetDate: null);
        ctx.AskShardAndReplyText(shard, userId, cmd, reply => reply switch
        {
            GoalAccepted a => TelegramReplies.GoalAdded(a.GoalId),
            GoalRejected r => $"Не удалось: {r.Reason}",
            _ => "Не понял ответа."
        }, "GoalAdd");
    }

    private static void HandleList(TelegramCommandContext ctx, IActorRef shard, Guid userId)
    {
        ctx.AskShardAndReplyText(shard, userId, new GetUserGoals(userId), reply => reply switch
        {
            UserGoalsList list => TelegramReplies.GoalList(list.Goals),
            _ => "Не понял ответа."
        }, "GoalList");
    }

    private static void HandleDone(TelegramCommandContext ctx, IActorRef shard, Guid userId, string indexStr)
    {
        if (!int.TryParse(indexStr.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var index) || index < 1)
        {
            ctx.Reply("Формат: `/goal done <номер>`. Используй `/goal list` чтобы увидеть номера.");
            return;
        }

        var chatId = ctx.Update.ChatId;
        var log = ctx.Log;
        var timeout = ctx.AskTimeout;
        var self = ctx.Self;

        Task.Run(async () =>
        {
            try
            {
                var goalsReply = await shard.Ask<object>(
                    new ShardEnvelope(userId.ToString("N"), new GetUserGoals(userId)), timeout)
                    .ConfigureAwait(false);

                if (goalsReply is not UserGoalsList goalsList)
                {
                    return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, "Внутренняя ошибка. Попробуй позже.")]);
                }

                var activeGoals = goalsList.Goals.Where(g => !g.IsCompleted).ToList();
                if (index > activeGoals.Count)
                {
                    return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, TelegramReplies.GoalNotFound())]);
                }

                var goalId = activeGoals[index - 1].GoalId;
                var completeReply = await shard.Ask<object>(
                    new ShardEnvelope(userId.ToString("N"), new CompleteGoal(userId, goalId)), timeout)
                    .ConfigureAwait(false);

                var text = completeReply switch
                {
                    GoalCompletedReply => TelegramReplies.GoalDone(),
                    GoalRejected r => $"Не удалось: {r.Reason}",
                    _ => "Не понял ответа."
                };
                return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, text)]);
            }
            catch (Exception ex)
            {
                log.Error(ex, "GoalDone failed for userId={UserId}.", userId);
                return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, "Внутренняя ошибка. Попробуй позже.")]);
            }
        })
        .PipeTo(self);
    }
}
