using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinanceBot.Infrastructure.Persistence;

/// <summary>
/// Design-time factory для команд <c>dotnet ef migrations …</c>.
/// Берёт строку подключения из ENV <c>ConnectionStrings__Default</c> или фиксированного fallback'а.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=financebot;Username=financebot;Password=change-me";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, b =>
            {
                b.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                b.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.AppSchema);
            })
            .Options;

        return new AppDbContext(options);
    }
}
