using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FinanceBot.Infrastructure.Telegram;

/// <summary>
/// Адаптер над Telegram.Bot.TelegramBotClient, реализующий <see cref="ITelegramBot"/>.
/// При пустом токене не падает, а логирует — удобно для запуска в dev/CI без реальной интеграции.
/// </summary>
public sealed class TelegramBotAdapter : ITelegramBot
{
    private readonly TelegramBotClient? _client;
    private readonly ILogger<TelegramBotAdapter> _log;

    public TelegramBotAdapter(IOptions<TelegramOptions> options, ILogger<TelegramBotAdapter> log)
    {
        ArgumentNullException.ThrowIfNull(options);
        _log = log;

        var token = options.Value.BotToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            _client = null;
            _log.LogWarning("Telegram:BotToken is empty — TelegramBotAdapter will NOOP all calls.");
            return;
        }

        _client = new TelegramBotClient(token);
    }

    public async Task SendTextAsync(long chatId, string text, CancellationToken ct)
    {
        if (_client is null)
        {
            _log.LogInformation("[stub] Send to chat={ChatId}: {Text}", chatId, text);
            return;
        }

        await _client.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.None,
            disableNotification: false,
            cancellationToken: ct);
    }

    public async Task<TelegramPollResult> PollAsync(long offset, TimeSpan timeout, CancellationToken ct)
    {
        if (_client is null)
        {
            await Task.Delay(timeout, ct);
            return new TelegramPollResult([], offset);
        }

        var updates = await _client.GetUpdates(
            offset: (int)offset,
            limit: 100,
            timeout: (int)timeout.TotalSeconds,
            allowedUpdates: [UpdateType.Message],
            cancellationToken: ct);

        var converted = new List<IncomingTelegramUpdate>(updates.Length);
        long nextOffset = offset;

        foreach (var update in updates)
        {
            nextOffset = Math.Max(nextOffset, update.Id + 1);
            var message = update.Message;
            if (message is null || message.From is null)
            {
                continue;
            }

            converted.Add(new IncomingTelegramUpdate(
                UpdateId: update.Id,
                ChatId: message.Chat.Id,
                TelegramId: message.From.Id,
                Username: message.From.Username,
                FirstName: message.From.FirstName,
                LastName: message.From.LastName,
                Text: message.Text,
                SentAt: new DateTimeOffset(message.Date, TimeSpan.Zero)));
        }

        return new TelegramPollResult(converted, nextOffset);
    }
}
