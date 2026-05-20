using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.Singleton;
using Akka.Hosting;
using Akka.Remote.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Scheduler;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.UserPlannedExpenses;
using FinanceBot.Application.Actors.UserTemplates;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Projections;
using FinanceBot.Application.Scheduling;
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
                    resolver.GetService<FinanceBot.Application.Projections.ISystemHeartbeatWriter>(),
                    resolver.GetService<IUserDirectory>(),
                    resolver.GetService<IUserScheduleResolver>()),
            options: new ClusterSingletonOptions { Role = null });

        builder.WithSingleton<FinanceBot.Application.Actors.Claude.ClaudeConsultantSingletonMarker>(
            singletonName: "claude-consultant",
            propsFactory: (_, _, resolver) =>
                FinanceBot.Application.Actors.Claude.ClaudeConsultantActor.CreateProps(
                    resolver.GetService<FinanceBot.Domain.Services.IClaudeClient>(),
                    resolver.GetService<IOptions<FinanceBot.Application.Actors.Claude.ClaudeConsultantOptions>>()),
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
                    resolver.GetService<FinanceBot.Domain.Services.ICategoryBucketMap>())),
            options: new ClusterSingletonOptions { Role = null });
    }

    private static void ConfigurePerNodeServices(AkkaConfigurationBuilder builder)
    {
        builder.WithActors((system, registry, resolver) =>
        {
            var gateway = system.ActorOf(TelegramGatewayActor.CreateProps(), "telegram-gateway");
            registry.Register<TelegramGatewayActor>(gateway);

            var categorizer = system.ActorOf(
                FinanceBot.Application.Actors.Categorizer.CategorizerActor.CreateProps(
                    resolver.GetService<FinanceBot.Domain.Services.ICategoryRules>()),
                "categorizer");
            registry.Register<FinanceBot.Application.Actors.Categorizer.CategorizerActorMarker>(categorizer);

            var advisor = system.ActorOf(
                FinanceBot.Application.Actors.Advisor.AdvisorActor.CreateProps(
                    resolver.GetService<FinanceBot.Application.Actors.Advisor.IAdvisorSnapshotReader>()),
                "advisor");
            registry.Register<FinanceBot.Application.Actors.Advisor.AdvisorActorMarker>(advisor);

            var chartPool = system.ActorOf(
                FinanceBot.Application.Actors.Charts.ChartRendererActor.CreatePoolProps(
                    resolver.GetService<FinanceBot.Application.Actors.Charts.IChartRenderer>(),
                    workers: 4),
                "chart-renderer-pool");
            registry.Register<FinanceBot.Application.Actors.Charts.ChartRendererPoolMarker>(chartPool);
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
