using Testcontainers.PostgreSql;
using Xunit;

namespace FinanceBot.Integration.Tests.Fixtures;

/// <summary>
/// Один Postgres-контейнер на всю test-сессию (collection fixture).
/// Если Docker недоступен — <see cref="IsAvailable"/> = false, и тесты скипаются через <c>Skip.IfNot</c>.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>Connection string к default-БД <c>postgres</c>. Тесты создают собственные БД через эту.</summary>
    public string AdminConnectionString { get; private set; } = string.Empty;

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("postgres")
                .WithUsername("financebot")
                .WithPassword("financebot")
                .Build();
            await _container.StartAsync();
            AdminConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = $"Docker/Testcontainers недоступен: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is null)
        {
            return;
        }
        try
        {
            await _container.DisposeAsync();
        }
        catch
        {
            // глушим: контейнер мог не стартовать.
        }
    }
}

[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "postgres";
}
