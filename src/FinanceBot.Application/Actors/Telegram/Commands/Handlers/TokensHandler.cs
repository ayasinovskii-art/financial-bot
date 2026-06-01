using System.Globalization;
using System.Text;
using Akka.Actor;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.Services;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

/// <summary>
/// /tokens — admin-only. Дёргает Claude минимальным ping-запросом и читает rate-limit заголовки
/// (anthropic-ratelimit-tokens-remaining / reset). Anthropic API не отдаёт credit balance напрямую,
/// поэтому показываем per-minute лимиты на API ключ.
/// </summary>
public sealed class TokensHandler(IClaudeClient claude) : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Tokens;

    public void Execute(TelegramCommandContext ctx)
    {
        if (!WhitelistShared.RequireAdmin(ctx))
        {
            return;
        }

        var chatId = ctx.Update.ChatId;
        var self = ctx.Self;
        var request = new ClaudeRequest(
            UseCase: ClaudeUseCase.Categorization,
            SystemPrompt: "pong",
            UserPrompt: "ping",
            MaxTokens: 1,
            CorrelationId: Guid.NewGuid());

        ctx.Reply("Проверяю лимиты Claude API…");

        claude.SendAsync(request, CancellationToken.None)
            .ContinueWith(t =>
            {
                var text = t.IsCompletedSuccessfully
                    ? Format(t.Result)
                    : $"Не удалось выполнить ping: {t.Exception?.GetBaseException().Message ?? "ошибка"}.";
                return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, text)]);
            })
            .PipeTo(self);
    }

    private static string Format(ClaudeResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Лимиты Claude API (per-minute window для ключа):");
        sb.Append("• tokens remaining: ")
          .AppendLine(r.TokensRemaining is { } rem ? rem.ToString(CultureInfo.InvariantCulture) : "n/a");
        if (r.TokensResetAt is { } reset)
        {
            sb.Append("• reset at: ").AppendLine(reset.UtcDateTime.ToString("u", CultureInfo.InvariantCulture));
        }
        sb.Append("• latency: ").Append(r.LatencyMs).AppendLine(" ms");

        if (r.IsSuccess)
        {
            sb.Append("• ping usage: input=")
              .Append(r.InputTokens?.ToString(CultureInfo.InvariantCulture) ?? "?")
              .Append(" output=")
              .Append(r.OutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "?");
        }
        else
        {
            sb.AppendLine();
            sb.Append("Ping провалился: ")
              .Append(r.FailureReason)
              .Append(" — ")
              .Append(r.FailureMessage);
        }
        return sb.ToString().TrimEnd();
    }
}
