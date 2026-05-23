using Akka.Actor;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

/// <summary>
/// Handlers, отвечающие через EventStream (UserActor публикует <see cref="OutgoingTelegramReply"/> сам).
/// Шлём Tell в shard и сразу подтверждаем пользователю "готовлю…".
/// </summary>
public sealed class AdviceHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Advice;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var scope = string.IsNullOrWhiteSpace(ctx.ArgumentLine) ? null : ctx.ArgumentLine.Trim();
        shard.Tell(new ShardEnvelope(userId.ToString("N"),
            new RequestConsultation(userId, Prompt: string.Empty, Scope: scope)));

        ctx.Reply(scope is null ? "Готовлю совет…" : $"Готовлю {scope}-обзор…");
    }
}

public sealed class ChartHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Chart;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var chartType = string.IsNullOrWhiteSpace(ctx.ArgumentLine) ? "category" : ctx.ArgumentLine.Trim();
        shard.Tell(new ShardEnvelope(userId.ToString("N"),
            new RequestChart(userId, chartType, Params: null)));
    }
}

public sealed class ReportHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Report;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }
        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var period = string.IsNullOrWhiteSpace(ctx.ArgumentLine) ? null : ctx.ArgumentLine.Trim();
        shard.Tell(new ShardEnvelope(userId.ToString("N"),
            new RequestReport(userId, period)));
    }
}
