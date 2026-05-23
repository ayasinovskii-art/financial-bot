using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Configuration;

namespace FinanceBot.Application.Actors.Telegram.Commands;

/// <summary>
/// Хелперы для типового Ask→Reply pattern'а: каждый handler формирует closure,
/// которая принимает (reply, exception) и возвращает текст для пользователя
/// (или <c>null</c> чтобы не отвечать). Хелпер сам делает PipeTo в <c>ctx.Self</c>.
/// </summary>
internal static class TelegramCommandHelpers
{
    /// <summary>Generic Ask с маппингом reply→текст. Ошибки логируются и возвращают «Внутренняя ошибка».</summary>
    public static void AskAndReplyText(
        this TelegramCommandContext ctx,
        IActorRef target,
        object message,
        Func<object?, string?> formatReply,
        string operationName)
        => target.Ask<object>(message, ctx.AskTimeout)
            .ContinueWith(t => BuildCompletion(ctx, operationName, t, formatReply))
            .PipeTo(ctx.Self);

    /// <summary>Удобная обёртка для команд, идущих через shard region.</summary>
    public static void AskShardAndReplyText(
        this TelegramCommandContext ctx,
        IActorRef shard,
        Guid userId,
        object command,
        Func<object?, string?> formatReply,
        string operationName)
        => ctx.AskAndReplyText(shard, new ShardEnvelope(userId.ToString("N"), command), formatReply, operationName);

    /// <summary>Полностью кастомный Ask: closure возвращает любые outgoing-сообщения (могут быть keyboards, callback-ack и т.п.).</summary>
    public static void AskAndDispatch(
        this TelegramCommandContext ctx,
        IActorRef target,
        object message,
        Func<object?, AggregateException?, IEnumerable<object>> buildOutgoing)
        => target.Ask<object>(message, ctx.AskTimeout)
            .ContinueWith(t => new TelegramCommandCompleted(
                buildOutgoing(
                    t.IsCompletedSuccessfully ? t.Result : null,
                    t.IsFaulted ? t.Exception : null)))
            .PipeTo(ctx.Self);

    /// <summary>Отправить одно текстовое сообщение в чат (через actor-loop).</summary>
    public static void Reply(this TelegramCommandContext ctx, string text)
        => ctx.Self.Tell(new OutgoingTelegramReply(ctx.Update.ChatId, text));

    /// <summary>Получить shard region по marker-типу или прислать "внутреннюю ошибку".</summary>
    public static IActorRef? GetShard<TMarker>(this TelegramCommandContext ctx) where TMarker : class
    {
        var registry = Akka.Hosting.ActorRegistry.For(ctx.System);
        if (!registry.TryGet<TMarker>(out var actor))
        {
            ctx.Log.Error("Shard region {Marker} not registered.", typeof(TMarker).Name);
            return null;
        }
        return actor;
    }

    private static TelegramCommandCompleted BuildCompletion(
        TelegramCommandContext ctx,
        string operationName,
        Task<object> task,
        Func<object?, string?> formatReply)
    {
        var chatId = ctx.Update.ChatId;
        if (task.IsFaulted)
        {
            ctx.Log.Error(task.Exception, "{Op} failed for telegramId={TelegramId}.", operationName, ctx.Update.TelegramId);
            return new TelegramCommandCompleted([
                new OutgoingTelegramReply(chatId, "Внутренняя ошибка. Попробуй позже.")
            ]);
        }
        var text = formatReply(task.IsCompletedSuccessfully ? task.Result : null);
        return text is null
            ? new TelegramCommandCompleted([])
            : new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, text)]);
    }
}
