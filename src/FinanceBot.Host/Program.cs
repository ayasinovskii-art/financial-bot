using Akka.Persistence.Hosting;
using Akka.Persistence.PostgreSql.Hosting;
using FinanceBot.Application;
using FinanceBot.Host;
using FinanceBot.Infrastructure;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Telegram;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.IncludeScopes = false;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
});

builder.Services.AddFinanceBotInfrastructure(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.AddFinanceBotApplication(builder.Configuration, (akka, _) =>
{
    akka.WithPostgreSqlPersistence(
        connectionString: connectionString,
        schemaName: "akka",
        autoInitialize: true);
});

builder.Services.AddHostedService<DatabaseMigrationService>();
builder.Services.AddHostedService<TelegramReplyDispatcher>();

var telegramMode = builder.Configuration.GetValue<string>("Telegram:Mode") ?? "Polling";
if (string.Equals(telegramMode, "Webhook", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<TelegramWebhookSetupService>();
}
else
{
    builder.Services.AddHostedService<TelegramPollingHostedService>();
}

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapGet("/", () => Results.Ok(new { service = "FinanceBot", status = "ok" }));

if (string.Equals(telegramMode, "Webhook", StringComparison.OrdinalIgnoreCase))
{
    app.MapTelegramWebhook();
}

app.Run();
