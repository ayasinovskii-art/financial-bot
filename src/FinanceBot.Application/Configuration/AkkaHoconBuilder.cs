using System.Text;
using Akka.Configuration;

namespace FinanceBot.Application.Configuration;

/// <summary>
/// Сборщик HOCON для Akka.Persistence.PostgreSql + serialization + event adapters.
/// Все настройки уровня cluster/remoting задаются через Akka.Hosting builder, а здесь — только то, что Hosting не покрывает напрямую.
/// </summary>
internal static class AkkaHoconBuilder
{
    public static Config BuildPersistenceHocon()
    {
        // Используется в дополнение к WithPostgreSqlPersistence — навешивает event-adapters на journal,
        // а также подключает Hyperion для сериализации доменных событий и сообщений.
        var sb = new StringBuilder();

        sb.AppendLine("""
            akka.persistence.journal.postgresql {
                event-adapters {
                    event-tagger = "FinanceBot.Application.Configuration.EventTagger, FinanceBot.Application"
                }
                event-adapter-bindings {
                    "FinanceBot.Domain.Events.IDomainEvent, FinanceBot.Domain" = event-tagger
                }
            }
            akka.persistence.query.journal.sql {
                class = "Akka.Persistence.Query.Sql.SqlReadJournalProvider, Akka.Persistence.Query.Sql"
                plugin-dispatcher = "akka.actor.default-dispatcher"
                write-plugin = "akka.persistence.journal.postgresql"
                refresh-interval = 1s
                max-buffer-size = 100
            }
            """);

        return ConfigurationFactory.ParseString(sb.ToString());
    }
}
