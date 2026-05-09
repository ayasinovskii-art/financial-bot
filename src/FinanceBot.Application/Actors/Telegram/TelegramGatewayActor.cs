using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.Common;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Per-node service: точка входа для Telegram update'ов.
/// На Stage 4 — skeleton, отвечает на Ping и логирует входящие.
/// На Stage 5 — добавлена реальная обработка команд /start, /help, /whoami, /cancel
/// (через partial-метод <c>WireStage5</c>, см. <c>TelegramGatewayActor.Stage5.cs</c>).
/// </summary>
public sealed partial class TelegramGatewayActor : ReceiveActor
{
    private readonly ILoggingAdapter _log;

    public TelegramGatewayActor()
    {
        _log = Context.GetLogger();

        Receive<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));

        WireStage5();

        ReceiveAny(msg => _log.Debug("TelegramGatewayActor received unhandled {MessageType}", msg.GetType().Name));
    }

    /// <summary>Stage 5 wiring. Реализация — в <c>TelegramGatewayActor.Stage5.cs</c>.</summary>
    partial void WireStage5();

    public static Props CreateProps() => Props.Create<TelegramGatewayActor>();
}
