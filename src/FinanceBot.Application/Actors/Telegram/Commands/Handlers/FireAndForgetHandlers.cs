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
        var (scope, question) = ParseAdviceArguments(ctx.ArgumentLine);
        shard.Tell(new ShardEnvelope(userId.ToString("N"),
            new RequestConsultation(userId, Prompt: question ?? string.Empty, Scope: scope)));

        ctx.Reply(scope switch
        {
            "week" or "weekly" => "Готовлю еженедельный обзор…",
            "month" or "monthly" => "Готовлю ежемесячный обзор…",
            "clear" => "Контекст советов очищен.",
            _ => "Готовлю совет…"
        });
    }

    internal static (string? Scope, string? Question) ParseAdviceArguments(string? argumentLine)
    {
        if (string.IsNullOrWhiteSpace(argumentLine))
        {
            return (null, null);
        }
        var trimmed = argumentLine.Trim();
        var spaceIdx = trimmed.IndexOfAny([' ', '\t', '\n']);
        var firstWord = (spaceIdx < 0 ? trimmed : trimmed[..spaceIdx]).ToLowerInvariant();
        var rest = spaceIdx < 0 ? string.Empty : trimmed[(spaceIdx + 1)..].Trim();

        return firstWord switch
        {
            "week" or "weekly" or "month" or "monthly" =>
                (firstWord, string.IsNullOrWhiteSpace(rest) ? null : rest),
            "clear" or "reset" => ("clear", null),
            _ => (null, trimmed)
        };
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

public sealed class StatsHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Stats;

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
            new RequestStats(userId, period)));
    }
}

public sealed class ExportHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Export;

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
            new RequestExport(userId, period)));
    }
}
