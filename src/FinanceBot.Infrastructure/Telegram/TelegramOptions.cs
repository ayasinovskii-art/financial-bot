namespace FinanceBot.Infrastructure.Telegram;

/// <summary>Опции Telegram (Telegram-секция appsettings).</summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>Токен бота от @BotFather. Пустая строка = реальный API не используется (dev/CI).</summary>
    public string BotToken { get; init; } = string.Empty;

    /// <summary>Polling | Webhook.</summary>
    public string Mode { get; init; } = "Polling";

    public string WebhookUrl { get; init; } = string.Empty;

    public int WebhookListenPort { get; init; } = 8443;
}
