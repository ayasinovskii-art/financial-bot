using Akka.Actor;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Telegram;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Host;

/// <summary>
/// Подписывается на исходящие Telegram-сообщения в Akka EventStream и пробрасывает в <see cref="ITelegramBot"/>.
/// Поддерживает текстовые reply, inline-клавиатуры и подтверждение callback'ов.
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
        actorSystem.EventStream.Subscribe(_subscriber, typeof(OutgoingInlineKeyboard));
        actorSystem.EventStream.Subscribe(_subscriber, typeof(OutgoingCallbackAck));
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
            Receive<OutgoingTelegramReply>(reply => _ = SendTextAsync(bot, log, reply));
            Receive<OutgoingInlineKeyboard>(kb => _ = SendKeyboardAsync(bot, log, kb));
            Receive<OutgoingCallbackAck>(ack => _ = AnswerCallbackAsync(bot, log, ack));
        }

        private static async Task SendTextAsync(ITelegramBot bot, ILogger log, OutgoingTelegramReply reply)
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

        private static async Task SendKeyboardAsync(ITelegramBot bot, ILogger log, OutgoingInlineKeyboard kb)
        {
            try
            {
                await bot.SendInlineKeyboardAsync(kb.ChatId, kb.Text, kb.Rows, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to deliver telegram inline-keyboard to chat={ChatId}.", kb.ChatId);
            }
        }

        private static async Task AnswerCallbackAsync(ITelegramBot bot, ILogger log, OutgoingCallbackAck ack)
        {
            try
            {
                await bot.AnswerCallbackAsync(ack.CallbackQueryId, ack.Text, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to answer callback {Id}.", ack.CallbackQueryId);
            }
        }

        public static Props CreateProps(ITelegramBot bot, ILogger log) =>
            Props.Create(() => new DispatcherActor(bot, log));
    }
}
