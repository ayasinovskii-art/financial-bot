using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.Hosting;
using Akka.Remote.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Advisor;
using FinanceBot.Application.Actors.Categorizer;
using FinanceBot.Application.Actors.Charts;
using FinanceBot.Application.Actors.Claude;
using FinanceBot.Application.Actors.Scheduler;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.Telegram.Commands;
using FinanceBot.Application.Actors.Telegram.Commands.Handlers;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.UserPlannedExpenses;
using FinanceBot.Application.Actors.UserTemplates;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Projections;
using FinanceBot.Application.Scheduling;
using FinanceBot.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinanceBot.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddFinanceBotApplication(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AkkaConfigurationBuilder, IServiceProvider> postgresPersistenceConfigurator)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(postgresPersistenceConfigurator);

        services.AddOptions<AkkaOptions>()
            .Bind(configuration.GetSection(AkkaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(o => o.AdminUserIds.Length > 0, "Auth:AdminUserIds must not be empty.")
            .ValidateOnStart();

        services.AddOptions<UserDefaultsOptions>()
            .Bind(configuration.GetSection(UserDefaultsOptions.SectionName));

        RegisterTelegramCommandHandlers(services);

        services.AddOptions<SchedulerOptions>()
            .Bind(configuration.GetSection(SchedulerOptions.SectionName));

        var akkaOptions = configuration
            .GetSection(AkkaOptions.SectionName)
            .Get<AkkaOptions>() ?? new AkkaOptions();

        services.AddAkka(akkaOptions.ClusterName, (builder, sp) =>
        {
            ConfigureRemotingAndCluster(builder, akkaOptions);
            postgresPersistenceConfigurator(builder, sp);
            builder.AddHocon(AkkaHoconBuilder.BuildPersistenceHocon(), HoconAddMode.Append);

            ConfigureSerialization(builder);
            ConfigureShardRegions(builder, akkaOptions);
            ConfigureSingletons(builder, sp);
            ConfigurePerNodeServices(builder);
        });

        return services;
    }

    private static void ConfigureRemotingAndCluster(AkkaConfigurationBuilder builder, AkkaOptions options)
    {
        builder.WithRemoting(options.Hostname, options.Port);

        var seeds = options.Cluster.SeedNodes is { Length: > 0 }
            ? options.Cluster.SeedNodes
            : [$"akka.tcp://{options.ClusterName}@{options.Hostname}:{options.Port}"];

        builder.WithClustering(new ClusterOptions
        {
            SeedNodes = seeds,
            MinimumNumberOfMembers = options.Cluster.MinimumMembers
        });
    }

    private static void ConfigureSerialization(AkkaConfigurationBuilder builder)
    {
        // Hyperion включён через WithPostgreSqlPersistence(... StoredAsType.Object) в Host'е,
        // отдельная регистрация сериализатора здесь не требуется на Stage 4.
        _ = builder;
    }

    private static void ConfigureShardRegions(AkkaConfigurationBuilder builder, AkkaOptions options)
    {
        var extractor = new UserShardMessageExtractor(options.Cluster.ShardCount);

        builder.WithShardRegion<UserShardMarker>(
            typeName: ShardRegionNames.User,
            entityPropsFactory: (_, _, _) => UserActor.CreatePropsFromEntityId,
            messageExtractor: extractor,
            shardOptions: new ShardOptions { Role = null, StateStoreMode = StateStoreMode.DData });

        builder.WithShardRegion<UserTemplatesShardMarker>(
            typeName: ShardRegionNames.UserTemplates,
            entityPropsFactory: (_, _, _) => UserTemplatesActor.CreatePropsFromEntityId,
            messageExtractor: extractor,
            shardOptions: new ShardOptions { Role = null, StateStoreMode = StateStoreMode.DData });

        builder.WithShardRegion<UserPlannedShardMarker>(
            typeName: ShardRegionNames.UserPlannedExpenses,
            entityPropsFactory: (_, _, _) => UserPlannedExpensesActor.CreatePropsFromEntityId,
            messageExtractor: extractor,
            shardOptions: new ShardOptions { Role = null, StateStoreMode = StateStoreMode.DData });
    }

    private static void ConfigureSingletons(AkkaConfigurationBuilder builder, IServiceProvider sp)
    {
        builder.WithSingleton<AccessControlSingletonMarker>(
            singletonName: "access-control",
            propsFactory: (_, _, resolver) =>
                AccessControlActor.CreateProps(resolver.GetService<IOptions<AuthOptions>>()),
            options: new ClusterSingletonOptions { Role = null });

        builder.WithSingleton<SchedulerSingletonMarker>(
            singletonName: "scheduler",
            propsFactory: (_, _, resolver) =>
                SchedulerActor.CreateProps(
                    resolver.GetService<ISystemHeartbeatWriter>(),
                    resolver.GetService<IUserDirectory>(),
                    resolver.GetService<IUserScheduleResolver>(),
                    resolver.GetService<IOptions<SchedulerOptions>>()),
            options: new ClusterSingletonOptions { Role = null });

        builder.WithSingleton<ClaudeConsultantSingletonMarker>(
            singletonName: "claude-consultant",
            propsFactory: (_, _, resolver) =>
                ClaudeConsultantActor.CreateProps(
                    resolver.GetService<IClaudeClient>(),
                    resolver.GetService<IOptions<ClaudeConsultantOptions>>()),
            options: new ClusterSingletonOptions { Role = null });

        builder.WithSingleton<UsersListProjectionMarker>(
            singletonName: "users-list-projection",
            propsFactory: (_, _, resolver) =>
                Akka.Actor.Props.Create(() => new UsersListProjection(
                    resolver.GetService<IProjectionOffsetStore>(),
                    resolver.GetService<IUsersReadModelWriter>())),
            options: new ClusterSingletonOptions { Role = null });

        builder.WithSingleton<WhitelistProjectionMarker>(
            singletonName: "whitelist-projection",
            propsFactory: (_, _, resolver) =>
                Akka.Actor.Props.Create(() => new WhitelistProjection(
                    resolver.GetService<IProjectionOffsetStore>(),
                    resolver.GetService<IWhitelistReadModelWriter>())),
            options: new ClusterSingletonOptions { Role = null });

        builder.WithSingleton<IncomeProjectionMarker>(
            singletonName: "income-projection",
            propsFactory: (_, _, resolver) =>
                Akka.Actor.Props.Create(() => new IncomeProjection(
                    resolver.GetService<IProjectionOffsetStore>(),
                    resolver.GetService<IIncomeReadModelWriter>())),
            options: new ClusterSingletonOptions { Role = null });

        builder.WithSingleton<PeriodProjectionMarker>(
            singletonName: "period-projection",
            propsFactory: (_, _, resolver) =>
                Akka.Actor.Props.Create(() => new PeriodProjection(
                    resolver.GetService<IProjectionOffsetStore>(),
                    resolver.GetService<IPeriodReadModelWriter>())),
            options: new ClusterSingletonOptions { Role = null });

        builder.WithSingleton<ExpenseProjectionMarker>(
            singletonName: "expense-projection",
            propsFactory: (_, _, resolver) =>
                Akka.Actor.Props.Create(() => new ExpenseProjection(
                    resolver.GetService<IProjectionOffsetStore>(),
                    resolver.GetService<IExpenseReadModelWriter>(),
                    resolver.GetService<ICategoryBucketMap>())),
            options: new ClusterSingletonOptions { Role = null });
    }

    private static void RegisterTelegramCommandHandlers(IServiceCollection services)
    {
        services.AddSingleton<ITelegramCommandHandler, StartHandler>();
        services.AddSingleton<ITelegramCommandHandler, HelpHandler>();
        services.AddSingleton<ITelegramCommandHandler, WhoAmIHandler>();
        services.AddSingleton<ITelegramCommandHandler, CancelHandler>();
        services.AddSingleton<ITelegramCommandHandler, AddUserHandler>();
        services.AddSingleton<ITelegramCommandHandler, RemoveUserHandler>();
        services.AddSingleton<ITelegramCommandHandler, ListUsersHandler>();
        services.AddSingleton<ITelegramCommandHandler, SettingsHandler>();
        services.AddSingleton<ITelegramCommandHandler, IncomeHandler>();
        services.AddSingleton<ITelegramCommandHandler, ExpenseHandler>();
        services.AddSingleton<ITelegramCommandHandler, ExpenseDayHandler>();
        services.AddSingleton<ITelegramCommandHandler, CorrectHandler>();
        services.AddSingleton<ITelegramCommandHandler, TemplateHandler>();
        services.AddSingleton<ITelegramCommandHandler, PlanHandler>();
        services.AddSingleton<ITelegramCommandHandler, SavingsHandler>();
        services.AddSingleton<ITelegramCommandHandler, GoalHandler>();
        services.AddSingleton<ITelegramCommandHandler, AdviceHandler>();
        services.AddSingleton<ITelegramCommandHandler, ChartHandler>();
        services.AddSingleton<ITelegramCommandHandler, ReportHandler>();
        services.AddSingleton<ITelegramCommandHandler, StatsHandler>();
        services.AddSingleton<ITelegramCommandHandler, ExportHandler>();
        services.AddSingleton<ITelegramCommandHandler, TokensHandler>();

        services.AddSingleton<ITelegramCallbackHandler, CorrectionCallbackHandler>();
        services.AddSingleton<NlpPendingCache>();
        services.AddSingleton<ITelegramCallbackHandler, NlpClarifyCallbackHandler>();
    }

    private static void ConfigurePerNodeServices(AkkaConfigurationBuilder builder)
    {
        builder.WithActors((system, registry, resolver) =>
        {
            var gateway = system.ActorOf(
                TelegramGatewayActor.CreateProps(
                    resolver.GetService<IOptions<UserDefaultsOptions>>(),
                    resolver.GetService<IEnumerable<ITelegramCommandHandler>>(),
                    resolver.GetService<IEnumerable<ITelegramCallbackHandler>>(),
                    resolver.GetService<NlpPendingCache>()),
                "telegram-gateway");
            registry.Register<TelegramGatewayActor>(gateway);

            var categorizer = system.ActorOf(
                CategorizerActor.CreateProps(resolver.GetService<ICategoryRules>()),
                "categorizer");
            registry.Register<CategorizerActorMarker>(categorizer);

            var advisor = system.ActorOf(
                AdvisorActor.CreateProps(resolver.GetService<IAdvisorSnapshotReader>()),
                "advisor");
            registry.Register<AdvisorActorMarker>(advisor);

            var chartPool = system.ActorOf(
                ChartRendererActor.CreatePoolProps(
                    resolver.GetService<IChartRenderer>(),
                    workers: 4),
                "chart-renderer-pool");
            registry.Register<ChartRendererPoolMarker>(chartPool);
        });
    }
}

/// <summary>Marker-тип для регистрации shard region User в registry.</summary>
public sealed class UserShardMarker;
/// <summary>Marker-тип для регистрации shard region UserTemplates в registry.</summary>
public sealed class UserTemplatesShardMarker;
/// <summary>Marker-тип для регистрации shard region UserPlannedExpenses в registry.</summary>
public sealed class UserPlannedShardMarker;
/// <summary>Marker-тип для регистрации singleton AccessControlActor в registry.</summary>
public sealed class AccessControlSingletonMarker;
/// <summary>Marker-тип для регистрации singleton SchedulerActor в registry.</summary>
public sealed class SchedulerSingletonMarker;
