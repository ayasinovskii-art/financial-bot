using FinanceBot.Application.Projections;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Services;
using FinanceBot.Infrastructure.CategoryRules;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Projections;
using FinanceBot.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinanceBot.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="AppDbContext"/> с Npgsql-провайдером и health-check Postgres.
    /// </summary>
    public static IServiceCollection AddFinanceBotInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is missing. Set ConnectionStrings__Default env var.");

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.AppSchema);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            });
        });

        // AddDbContextFactory регистрирует AppDbContext как scoped за счёт Factory.
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(
                "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["db", "ready"]);

        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IProjectionOffsetStore, ProjectionOffsetStore>();
        services.AddSingleton<IUsersReadModelWriter, UsersReadModelWriter>();
        services.AddSingleton<IWhitelistReadModelWriter, WhitelistReadModelWriter>();
        services.AddSingleton<IIncomeReadModelWriter, IncomeReadModelWriter>();
        services.AddSingleton<IPeriodReadModelWriter, PeriodReadModelWriter>();
        services.AddSingleton<IExpenseReadModelWriter, ExpenseReadModelWriter>();
        services.AddSingleton<ICategoryBucketMap, DefaultCategoryBucketMap>();
        services.AddSingleton<ICategoryRules, JsonCategoryRules>();
        services.AddSingleton<ITelegramBot, TelegramBotAdapter>();

        return services;
    }
}
