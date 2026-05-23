using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Configuration;

namespace FinanceBot.Application.Actors.Telegram.Commands;

/// <summary>Контекст вызова <see cref="ITelegramCommandHandler.Execute"/>.</summary>
public sealed class TelegramCommandContext
{
    public required IncomingTelegramUpdate Update { get; init; }
    public required string ArgumentLine { get; init; }
    public required AccessDecision.Allowed Allowed { get; init; }
    public required IActorRef Self { get; init; }
    public required ActorSystem System { get; init; }
    public required ILoggingAdapter Log { get; init; }
    public required UserDefaultsOptions Defaults { get; init; }
    public required TimeSpan AskTimeout { get; init; }
}

/// <summary>Контекст вызова <see cref="ITelegramCallbackHandler.Execute"/>.</summary>
public sealed class TelegramCallbackContext
{
    public required IncomingCallbackQuery Callback { get; init; }
    public required IActorRef Self { get; init; }
    public required ActorSystem System { get; init; }
    public required ILoggingAdapter Log { get; init; }
    public required TimeSpan AskTimeout { get; init; }
}
