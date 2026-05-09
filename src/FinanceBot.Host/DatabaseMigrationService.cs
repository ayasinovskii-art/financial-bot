using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Host;

/// <summary>
/// На старте применяет EF миграции для схемы <c>app</c>.
/// Схема <c>akka</c> создаётся самим Akka.Persistence.PostgreSql (autoInitialize=true).
/// </summary>
public sealed class DatabaseMigrationService(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseMigrationService> log)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            log.LogInformation("Applying app-schema migrations…");
            await db.Database.MigrateAsync(cancellationToken);
            log.LogInformation("Migrations applied.");
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "Failed to apply migrations.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
