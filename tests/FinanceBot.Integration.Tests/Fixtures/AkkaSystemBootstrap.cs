using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Persistence.PostgreSql.Hosting;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FinanceBot.Integration.Tests.Fixtures;

/// <summary>
/// Поднимает <see cref="IHost"/> с Akka через ту же связку (Akka.Hosting + WithPostgreSqlPersistence),
/// которую использует production. Это гарантирует корректную сериализацию и инициализацию схемы.
/// </summary>
internal static class AkkaSystemBootstrap
{
    public static async Task<(IHost Host, ActorSystem System)> CreateAsync(
        string systemName, string connectionString, CancellationToken ct = default)
    {
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureServices(services =>
        {
            services.AddAkka(systemName, akka =>
            {
                akka.WithPostgreSqlPersistence(
                    connectionString: connectionString,
                    schemaName: "akka",
                    autoInitialize: true,
                    journalBuilder: jb =>
                    {
                        jb.AddWriteEventAdapter<EventTagger>(
                            eventAdapterName: "event-tagger",
                            boundTypes: new[] { typeof(IDomainEvent) });
                    });
                akka.AddHocon(
                    ConfigurationFactory.ParseString("""
                        akka {
                            loglevel = WARNING
                            persistence.query.journal.sql {
                                class = "Akka.Persistence.Query.Sql.SqlReadJournalProvider, Akka.Persistence.Query.Sql"
                                plugin-dispatcher = "akka.actor.default-dispatcher"
                                write-plugin = "akka.persistence.journal.postgresql"
                                refresh-interval = 250ms
                                max-buffer-size = 100
                            }
                        }
                        """),
                    HoconAddMode.Append);
            });
        });

        var host = builder.Build();
        await host.StartAsync(ct);
        var system = host.Services.GetRequiredService<ActorSystem>();
        return (host, system);
    }
}

