using Akka.Actor;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Telegram;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Host;

/// <summary>
/// Подписывается на <see cref="OutgoingTelegramReply"/> в Akka EventStream и пробрасывает в <see cref="ITelegramBot"/>.
/// </summary>
public sealed class TelegramReplyDispatcher(
    ActorSystem actorSystem,
    ITelegramBot telegramBot,
    ILogger<TelegramReplyDispatcher> log)
    : IHostedService
{
    private IActorRef? _subscriber;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriber = actorSystem.ActorOf(
            DispatcherActor.CreateProps(telegramBot, log),
            "telegram-reply-dispatcher");
        actorSystem.EventStream.Subscribe(_subscriber, typeof(OutgoingTelegramReply));
        log.LogInformation("Telegram reply dispatcher subscribed to EventStream.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null)
        {
            actorSystem.EventStream.Unsubscribe(_subscriber);
            _subscriber.Tell(PoisonPill.Instance);
        }
        return Task.CompletedTask;
    }

    private sealed class DispatcherActor : ReceiveActor
    {
        public DispatcherActor(ITelegramBot bot, ILogger log)
        {
            Receive<OutgoingTelegramReply>(reply =>
            {
                _ = SendAsync(bot, log, reply);
            });
        }

        private static async Task SendAsync(ITelegramBot bot, ILogger log, OutgoingTelegramReply reply)
        {
            try
            {
                await bot.SendTextAsync(reply.ChatId, reply.Text, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to deliver telegram reply to chat={ChatId}.", reply.ChatId);
            }
        }

        public static Props CreateProps(ITelegramBot bot, ILogger log) =>
            Props.Create(() => new DispatcherActor(bot, log));
    }
}
