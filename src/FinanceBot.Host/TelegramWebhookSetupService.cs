using FinanceBot.Application.Telegram;
using FinanceBot.Infrastructure.Telegram;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceBot.Host;

/// <summary>
/// При старте сервиса в Webhook-режиме регистрирует webhook URL у Telegram.
/// Стартует только если Telegram:Mode == Webhook и WebhookUrl задан.
/// </summary>
public sealed class TelegramWebhookSetupService(
    ITelegramBot bot,
    IOptions<TelegramOptions> options,
    ILogger<TelegramWebhookSetupService> log)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!string.Equals(opts.Mode, "Webhook", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(opts.WebhookUrl))
        {
            log.LogWarning("Telegram:WebhookUrl is empty — webhook registration skipped.");
            return;
        }

        log.LogInformation("Registering Telegram webhook: {Url}", opts.WebhookUrl);
        await bot.SetWebhookAsync(opts.WebhookUrl, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
