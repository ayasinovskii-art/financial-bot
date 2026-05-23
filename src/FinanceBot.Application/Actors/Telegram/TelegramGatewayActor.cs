using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Common;
using FinanceBot.Application.Actors.Telegram.Commands;
using FinanceBot.Application.Actors.Telegram.Commands.Handlers;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Configuration;
using Microsoft.Extensions.Options;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Per-node service: единственная точка входа Telegram update'ов.
/// Логика команд вынесена в <see cref="ITelegramCommandHandler"/> и <see cref="ITelegramCallbackHandler"/>.
/// Сам actor — тонкий диспетчер: парсит команду, проверяет whitelist (с in-memory cache),
/// выбирает handler по <see cref="TelegramCommandKind"/> / callback prefix и публикует outgoing-сообщения.
/// </summary>
public sealed class TelegramGatewayActor : ReceiveActor
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AccessCacheTtl = TimeSpan.FromSeconds(60);

    private readonly ILoggingAdapter _log;
    private readonly UserDefaultsOptions _defaults;
    private readonly IReadOnlyDictionary<TelegramCommandKind, ITelegramCommandHandler> _handlers;
    private readonly IReadOnlyList<ITelegramCallbackHandler> _callbackHandlers;
    private readonly Dictionary<long, (AccessDecision Decision, DateTimeOffset ExpiresAt)> _accessCache = new();

    public TelegramGatewayActor(
        IOptions<UserDefaultsOptions> defaults,
        IEnumerable<ITelegramCommandHandler> handlers,
        IEnumerable<ITelegramCallbackHandler> callbackHandlers)
    {
        _log = Context.GetLogger();
        _defaults = defaults?.Value ?? new UserDefaultsOptions();
        _handlers = handlers.ToDictionary(h => h.Kind);
        _callbackHandlers = callbackHandlers.ToList();

        Receive<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));

        Receive<IncomingTelegramUpdate>(HandleIncomingUpdate);
        Receive<IncomingCallbackQuery>(HandleIncomingCallback);
        Receive<AccessCheckResult>(OnAccessCheckResult);

        Receive<TelegramCommandCompleted>(OnCommandCompleted);

        Receive<OutgoingTelegramReply>(reply => Context.System.EventStream.Publish(reply));
        Receive<OutgoingInlineKeyboard>(kb => Context.System.EventStream.Publish(kb));
        Receive<OutgoingCallbackAck>(ack => Context.System.EventStream.Publish(ack));

        ReceiveAny(msg => _log.Debug("TelegramGatewayActor received unhandled {MessageType}", msg.GetType().Name));
    }

    public static Props CreateProps(
        IOptions<UserDefaultsOptions> defaults,
        IEnumerable<ITelegramCommandHandler> handlers,
        IEnumerable<ITelegramCallbackHandler> callbackHandlers)
        => Props.Create(() => new TelegramGatewayActor(defaults, handlers, callbackHandlers));

    private void HandleIncomingUpdate(IncomingTelegramUpdate update)
    {
        _log.Debug("Incoming update {UpdateId} corr={CorrelationId} from telegramId={TelegramId} text={Text}",
            update.UpdateId, update.CorrelationId, update.TelegramId, update.Text);

        var parsed = TelegramCommandParser.TryParse(update.Text);

        var now = DateTimeOffset.UtcNow;
        if (_accessCache.TryGetValue(update.TelegramId, out var cached) && cached.ExpiresAt > now)
        {
            Self.Tell(new AccessCheckResult(update, parsed, cached.Decision, null));
            return;
        }

        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<AccessControlSingletonMarker>(out var accessControl))
        {
            _log.Warning("AccessControlActor not available; dropping update {UpdateId} corr={CorrelationId}.",
                update.UpdateId, update.CorrelationId);
            return;
        }

        var self = Self;
        accessControl
            .Ask<AccessDecision>(new Domain.Commands.AccessControl.IsAllowed(update.TelegramId), AskTimeout)
            .ContinueWith(t => new AccessCheckResult(update, parsed,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    private void OnAccessCheckResult(AccessCheckResult msg)
    {
        if (msg.Exception is not null)
        {
            _log.Error(msg.Exception, "Access check failed for telegramId={TelegramId}", msg.Update.TelegramId);
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка. Попробуй позже."));
            return;
        }

        if (msg.Decision is not null)
        {
            _accessCache[msg.Update.TelegramId] = (msg.Decision, DateTimeOffset.UtcNow + AccessCacheTtl);
        }

        switch (msg.Decision)
        {
            case AccessDecision.Denied:
                Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, TelegramReplies.AccessDenied(msg.Update.TelegramId)));
                return;

            case AccessDecision.Allowed allowed:
                Dispatch(msg.Update, msg.Parsed, allowed);
                return;
        }
    }

    private void Dispatch(IncomingTelegramUpdate update, ParsedTelegramCommand? parsed, AccessDecision.Allowed allowed)
    {
        var ctx = new TelegramCommandContext
        {
            Update = update,
            ArgumentLine = parsed?.ArgumentLine ?? string.Empty,
            Allowed = allowed,
            Self = Self,
            System = Context.System,
            Log = _log,
            Defaults = _defaults,
            AskTimeout = AskTimeout
        };

        if (parsed is null)
        {
            FreeTextHandler.Execute(ctx);
            return;
        }

        if (_handlers.TryGetValue(parsed.Kind, out var handler))
        {
            handler.Execute(ctx);
            return;
        }

        Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.UnknownCommand()));
    }

    private void HandleIncomingCallback(IncomingCallbackQuery cb)
    {
        foreach (var handler in _callbackHandlers)
        {
            if (cb.Data.StartsWith(handler.DataPrefix, StringComparison.Ordinal))
            {
                handler.Execute(new TelegramCallbackContext
                {
                    Callback = cb,
                    Self = Self,
                    System = Context.System,
                    Log = _log,
                    AskTimeout = AskTimeout
                });
                return;
            }
        }
        _log.Debug("Unknown callback prefix: {Data}", cb.Data);
        Self.Tell(new OutgoingCallbackAck(cb.CallbackQueryId, null));
    }

    private void OnCommandCompleted(TelegramCommandCompleted msg)
    {
        foreach (var outgoing in msg.Outgoing)
        {
            Self.Tell(outgoing);
        }
    }

    private sealed record AccessCheckResult(
        IncomingTelegramUpdate Update,
        ParsedTelegramCommand? Parsed,
        AccessDecision? Decision,
        AggregateException? Exception);
}
