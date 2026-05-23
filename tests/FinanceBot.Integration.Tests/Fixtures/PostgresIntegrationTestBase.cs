using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FinanceBot.Integration.Tests.Fixtures;

/// <summary>
/// База для интеграционных тестов: создаёт уникальную БД в Postgres-контейнере,
/// применяет EF-миграции (схема <c>app</c>) и отдаёт connection string.
/// Схема <c>akka</c> создаётся самим Akka.Persistence.PostgreSql при первом обращении.
/// </summary>
public abstract class PostgresIntegrationTestBase : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private string _databaseName = string.Empty;

    protected PostgresIntegrationTestBase(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Connection string к свежесозданной тестовой БД.</summary>
    protected string ConnectionString { get; private set; } = string.Empty;

    public virtual async Task InitializeAsync()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason ?? "Docker недоступен.");

        _databaseName = $"test_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_fixture.AdminConnectionString)
        {
            Database = _databaseName
        };
        ConnectionString = builder.ToString();

        // 1. EF-миграции схемы `app` (read-model).
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        await using (var ctx = new AppDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        // 2. Создаём схему `akka` — Akka.Persistence.PostgreSql autoInitialize
        //    создаёт только таблицы внутри схемы, но не саму схему.
        await using (var conn = new NpgsqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE SCHEMA IF NOT EXISTS akka";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public virtual Task DisposeAsync()
    {
        // БД остаётся в контейнере; контейнер сносится в конце test-сессии.
        // Между тестами — изоляция через уникальное имя БД.
        return Task.CompletedTask;
    }

    protected AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}
