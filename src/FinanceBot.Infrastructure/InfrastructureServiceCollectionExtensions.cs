using FinanceBot.Application.Actors.Claude;
using FinanceBot.Application.Projections;
using FinanceBot.Application.Scheduling;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Services;
using FinanceBot.Infrastructure.CategoryRules;
using FinanceBot.Infrastructure.Claude;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Projections;
using FinanceBot.Infrastructure.Scheduling;
using FinanceBot.Infrastructure.Telegram;
using FinanceBot.Infrastructure.Timezone;
using FinanceBot.Infrastructure.WorkdayCalendar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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

        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(
                "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["db", "ready"])
            .AddCheck<FinanceBot.Infrastructure.Health.AkkaClusterHealthCheck>(
                "akka-cluster",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["akka", "ready"]);

        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<ClaudeOptions>()
            .Bind(configuration.GetSection(ClaudeOptions.SectionName))
            .ValidateOnStart();

        // Опции для ClaudeConsultantActor — производные от ClaudeOptions.Resilience.
        services.AddSingleton<IOptions<ClaudeConsultantOptions>>(sp =>
        {
            var claude = sp.GetRequiredService<IOptions<ClaudeOptions>>().Value;
            return Options.Create(new ClaudeConsultantOptions
            {
                ConcurrencyLimit = claude.Resilience.ConcurrencyLimit,
                TransientUnavailableUntilHour = claude.Resilience.TransientUnavailableUntilHour
            });
        });

        services.AddHttpClient<IClaudeClient, ClaudeClient>(http =>
        {
            http.BaseAddress = new Uri("https://api.anthropic.com");
            http.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddOptions<WorkdayCalendarOptions>()
            .Bind(configuration.GetSection(WorkdayCalendarOptions.SectionName))
            .ValidateOnStart();

        var calendarProvider = configuration.GetValue<string>("WorkdayCalendar:Provider")?.ToLowerInvariant() ?? "isdayoff";
        if (calendarProvider == "static")
        {
            services.AddSingleton<IWorkdayCalendar, StaticWorkdayCalendar>();
        }
        else
        {
            services.AddHttpClient<IWorkdayCalendar, IsDayOffWorkdayCalendar>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<WorkdayCalendarOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(10);
            });
        }

        services.AddSingleton<ITimezoneRegistry, TimezoneRegistry>();
        services.AddSingleton<ISystemHeartbeatWriter, SystemHeartbeatWriter>();
        services.AddSingleton<IUserDirectory, UserDirectoryReader>();
        services.AddSingleton<IUserScheduleResolver, UserScheduleResolver>();

        services.AddSingleton<IProjectionOffsetStore, ProjectionOffsetStore>();
        services.AddSingleton<IUsersReadModelWriter, UsersReadModelWriter>();
        services.AddSingleton<IWhitelistReadModelWriter, WhitelistReadModelWriter>();
        services.AddSingleton<IIncomeReadModelWriter, IncomeReadModelWriter>();
        services.AddSingleton<IPeriodReadModelWriter, PeriodReadModelWriter>();
        services.AddSingleton<IExpenseReadModelWriter, ExpenseReadModelWriter>();
        services.AddSingleton<ICategoryBucketMap, DefaultCategoryBucketMap>();
        services.AddSingleton<ICategoryRules, JsonCategoryRules>();
        services.AddSingleton<ITelegramBot, TelegramBotAdapter>();
        services.AddSingleton<FinanceBot.Application.Actors.Advisor.IAdvisorSnapshotReader,
            FinanceBot.Infrastructure.Advisor.AdvisorSnapshotReader>();
        services.AddSingleton<FinanceBot.Application.Actors.Charts.IChartDataReader,
            FinanceBot.Infrastructure.Charts.ChartDataReader>();
        services.AddSingleton<FinanceBot.Application.Actors.Charts.IChartRenderer,
            FinanceBot.Infrastructure.Charts.ScottPlotChartRenderer>();
        services.AddSingleton<FinanceBot.Application.Actors.Reports.IReportBuilder,
            FinanceBot.Infrastructure.Reports.ReportBuilder>();

        return services;
    }
}
