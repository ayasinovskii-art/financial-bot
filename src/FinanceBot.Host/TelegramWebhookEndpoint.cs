using Akka.Actor;
using Akka.Hosting;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Infrastructure.Telegram;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace FinanceBot.Host;

/// <summary>
/// HTTP endpoint для приёма Telegram webhook'ов. Telegram POST'ит JSON Update на
/// /telegram/webhook, а мы парсим его и пушим в TelegramGatewayActor.
/// Возвращает 200 OK сразу после успешной маршрутизации (Telegram повторит на 5xx).
/// </summary>
public static class TelegramWebhookEndpoint
{
    public static IEndpointConventionBuilder MapTelegramWebhook(this WebApplication app, string path = "/telegram/webhook")
        => app.MapPost(path, HandleAsync);

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ActorRegistry registry,
        ILogger<Program> log)
    {
        if (!registry.TryGet<TelegramGatewayActor>(out var gateway))
        {
            log.LogError("TelegramGatewayActor not registered; webhook payload dropped.");
            return Results.StatusCode(503);
        }

        Update? update;
        try
        {
            update = await context.Request.ReadFromJsonAsync<Update>(context.RequestAborted);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to deserialize Telegram update body.");
            return Results.BadRequest();
        }

        if (update is null)
        {
            return Results.BadRequest();
        }

        var (msg, callback) = TelegramUpdateConverter.TryConvert(update);
        if (msg is not null) gateway.Tell(msg);
        if (callback is not null) gateway.Tell(callback);
        return Results.Ok();
    }
}
