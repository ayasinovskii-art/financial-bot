using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

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

    public async Task SendInlineKeyboardAsync(
        long chatId,
        string text,
        IReadOnlyList<IReadOnlyList<InlineButton>> rows,
        CancellationToken ct)
    {
        if (_client is null)
        {
            _log.LogInformation("[stub] Send keyboard to chat={ChatId} ({RowCount} rows): {Text}",
                chatId, rows.Count, text);
            return;
        }

        var keyboardRows = rows
            .Select(r => r.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData)).ToArray())
            .ToArray();
        var markup = new InlineKeyboardMarkup(keyboardRows);

        await _client.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.None,
            replyMarkup: markup,
            cancellationToken: ct);
    }

    public async Task SendPhotoAsync(long chatId, byte[] photo, string fileName, string? caption, CancellationToken ct)
    {
        if (_client is null)
        {
            _log.LogInformation("[stub] Send photo to chat={ChatId}, size={Size} bytes, caption={Caption}",
                chatId, photo.Length, caption);
            return;
        }

        using var stream = new MemoryStream(photo, writable: false);
        var input = new InputFileStream(stream, fileName);
        await _client.SendPhoto(
            chatId: chatId,
            photo: input,
            caption: caption,
            cancellationToken: ct);
    }

    public async Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken ct)
    {
        if (_client is null)
        {
            _log.LogInformation("[stub] Answer callback {Id}: {Text}", callbackQueryId, text);
            return;
        }

        await _client.AnswerCallbackQuery(
            callbackQueryId: callbackQueryId,
            text: text,
            cancellationToken: ct);
    }

    /// <summary>Установить webhook URL. Возвращает true если установка прошла (или адаптер в stub-mode).</summary>
    public async Task<bool> SetWebhookAsync(string url, CancellationToken ct)
    {
        if (_client is null)
        {
            _log.LogInformation("[stub] SetWebhook: {Url}", url);
            return true;
        }
        await _client.SetWebhook(
            url: url,
            allowedUpdates: [global::Telegram.Bot.Types.Enums.UpdateType.Message, global::Telegram.Bot.Types.Enums.UpdateType.CallbackQuery],
            cancellationToken: ct);
        return true;
    }

    /// <summary>Удалить webhook (использовать перед переключением обратно на polling).</summary>
    public async Task DeleteWebhookAsync(CancellationToken ct)
    {
        if (_client is null) return;
        await _client.DeleteWebhook(cancellationToken: ct);
    }

    public async Task<TelegramPollResult> PollAsync(long offset, TimeSpan timeout, CancellationToken ct)
    {
        if (_client is null)
        {
            await Task.Delay(timeout, ct);
            return new TelegramPollResult([], [], offset);
        }

        var updates = await _client.GetUpdates(
            offset: (int)offset,
            limit: 100,
            timeout: (int)timeout.TotalSeconds,
            allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
            cancellationToken: ct);

        var convertedUpdates = new List<IncomingTelegramUpdate>(updates.Length);
        var convertedCallbacks = new List<IncomingCallbackQuery>(updates.Length);
        long nextOffset = offset;

        foreach (var update in updates)
        {
            nextOffset = Math.Max(nextOffset, update.Id + 1);
            var (converted, callback) = TelegramUpdateConverter.TryConvert(update);
            if (converted is not null) convertedUpdates.Add(converted);
            if (callback is not null) convertedCallbacks.Add(callback);
        }

        return new TelegramPollResult(convertedUpdates, convertedCallbacks, nextOffset);
    }
}
