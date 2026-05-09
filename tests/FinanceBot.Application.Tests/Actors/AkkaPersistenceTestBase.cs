using Akka.Configuration;
using Akka.TestKit.Xunit2;

namespace FinanceBot.Application.Tests.Actors;

/// <summary>
/// Базовый класс для тестов persistent акторов с in-memory journal/snapshot store.
/// </summary>
public abstract class AkkaPersistenceTestBase : TestKit
{
    protected AkkaPersistenceTestBase() : base(BuildConfig())
    {
    }

    private static Config BuildConfig() => ConfigurationFactory.ParseString("""
        akka {
            loglevel = "WARNING"
            actor.serialize-messages = off
            persistence {
                journal {
                    plugin = "akka.persistence.journal.inmem"
                    inmem.event-adapters {}
                    inmem.event-adapter-bindings {}
                }
                snapshot-store {
                    plugin = "akka.persistence.snapshot-store.local"
                    local.dir = "target/snapshots"
                }
            }
        }
        """);
}
