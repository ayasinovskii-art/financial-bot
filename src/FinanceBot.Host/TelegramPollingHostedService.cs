using Akka.Actor;
using Akka.Hosting;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Telegram;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Host;

/// <summary>
/// Long-polling сервис: тянет обновления из Telegram и пушит их в TelegramGatewayActor.
/// Запускается на каждом ноде; на multi-node-deployment оборачивается ClusterSingleton (Stage 22).
/// </summary>
public sealed class TelegramPollingHostedService(
    ITelegramBot telegramBot,
    ActorRegistry actorRegistry,
    ILogger<TelegramPollingHostedService> log)
    : BackgroundService
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Telegram polling service started.");

        if (!actorRegistry.TryGet<TelegramGatewayActor>(out var gateway))
        {
            log.LogError("TelegramGatewayActor not registered; polling aborted.");
            return;
        }

        long offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await telegramBot.PollAsync(offset, PollTimeout, stoppingToken);
                offset = result.NextOffset;

                foreach (var update in result.Updates)
                {
                    gateway.Tell(update);
                }
                foreach (var callback in result.Callbacks)
                {
                    gateway.Tell(callback);
                }
                foreach (var file in result.Files)
                {
                    gateway.Tell(file);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Telegram polling error; backing off {Backoff}.", ErrorBackoff);
                try
                {
                    await Task.Delay(ErrorBackoff, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        log.LogInformation("Telegram polling service stopped.");
    }
}
