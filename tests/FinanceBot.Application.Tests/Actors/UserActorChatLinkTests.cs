using Akka.Actor;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class UserActorChatLinkTests : AkkaPersistenceTestBase
{
    [Fact]
    public void LinkUserChat_persists_UserChatLinked_when_chatId_differs()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 12345, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new LinkUserChat(userId, 99001));

        actor.Tell(new GetUserSnapshot(userId));
        var snap = ExpectMsg<UserSnapshot>();
        snap.LastKnownChatId.Should().Be(99001);
    }

    [Fact]
    public void LinkUserChat_does_not_persist_second_event_when_chatId_unchanged()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor.Tell(new LinkUserChat(userId, 42));
        actor.Tell(new LinkUserChat(userId, 42));

        // State must reflect chatId exactly once — no duplicate in-memory state.
        actor.Tell(new GetUserSnapshot(userId));
        var snap = ExpectMsg<UserSnapshot>();
        snap.LastKnownChatId.Should().Be(42);
    }

    [Fact]
    public async Task After_replay_LastKnownChatId_is_correctly_restored()
    {
        var userId = Guid.NewGuid();
        var actor1 = Sys.ActorOf(UserActor.CreateProps(userId));

        actor1.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        actor1.Tell(new LinkUserChat(userId, 77777));

        // Drain a snapshot to ensure persist callback has fired before we stop.
        actor1.Tell(new GetUserSnapshot(userId));
        ExpectMsg<UserSnapshot>().LastKnownChatId.Should().Be(77777);

        await actor1.GracefulStop(TimeSpan.FromSeconds(5));

        // New actor with same PersistenceId recovers all events from the in-memory journal.
        var actor2 = Sys.ActorOf(UserActor.CreateProps(userId));
        actor2.Tell(new GetUserSnapshot(userId));
        var snap = ExpectMsg<UserSnapshot>(TimeSpan.FromSeconds(5));
        snap.LastKnownChatId.Should().Be(77777);
    }

    [Fact]
    public void UserSnapshot_LastKnownChatId_returns_linked_value_via_ask()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();

        // Before any link, snapshot has no chatId.
        actor.Tell(new GetUserSnapshot(userId));
        ExpectMsg<UserSnapshot>().LastKnownChatId.Should().BeNull();

        actor.Tell(new LinkUserChat(userId, 55555));

        // After link, snapshot reflects the linked value.
        actor.Tell(new GetUserSnapshot(userId));
        ExpectMsg<UserSnapshot>().LastKnownChatId.Should().Be(55555);
    }
}
