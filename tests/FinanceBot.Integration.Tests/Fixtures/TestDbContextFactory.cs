using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Integration.Tests.Fixtures;

/// <summary>
/// <see cref="IDbContextFactory{TContext}"/> для тестов: каждый вызов отдаёт новый
/// контекст, подключённый к тестовой БД (по connection string).
/// </summary>
internal sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AppDbContext(options);
    }
}
