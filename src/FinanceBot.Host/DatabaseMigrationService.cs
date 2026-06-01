using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Host;

/// <summary>
/// На старте применяет EF миграции для схемы <c>app</c> и создаёт пустую схему <c>akka</c>.
/// Akka.Persistence.PostgreSql с autoInitialize=true создаёт сами таблицы, но не схему,
/// поэтому без CREATE SCHEMA IF NOT EXISTS akka snapshot-store падает с 3F000.
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
            log.LogInformation("Ensuring 'akka' schema exists…");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE SCHEMA IF NOT EXISTS akka;",
                cancellationToken);

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
