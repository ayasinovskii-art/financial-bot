using Akka.Actor;
using Akka.Persistence;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Events.Advisor;
using FinanceBot.Domain.Events.Claude;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class UserAdviceActorRecoveryTests : AkkaPersistenceTestBase
{
    [Fact]
    public async Task Recovery_restores_QA_pairs_from_journal()
    {
        var userId = Guid.NewGuid();
        var corr1 = Guid.NewGuid();
        var corr2 = Guid.NewGuid();

        await SeedEventsAsync($"user-{userId:N}-advice", new object[]
        {
            new ConsultationRequested(userId, corr1, "p", AdvisorTickType.OnDemand,
                DateTimeOffset.UtcNow - TimeSpan.FromMinutes(40), UserQuestion: "вопрос1"),
            new ConsultationAnswered(userId, corr1, "ответ1",
                ConsultationSource.Claude, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(39)),
            new ConsultationRequested(userId, corr2, "p", AdvisorTickType.OnDemand,
                DateTimeOffset.UtcNow - TimeSpan.FromMinutes(20), UserQuestion: "вопрос2"),
            new ConsultationAnswered(userId, corr2, "ответ2",
                ConsultationSource.Claude, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(19)),
        });

        var actor = Sys.ActorOf(UserAdviceActor.CreateProps(userId));
        actor.Tell(new GetAdviceConversation(userId));
        var state = ExpectMsg<AdviceConversationState>(TimeSpan.FromSeconds(5));

        state.Turns.Should().HaveCount(2);
        state.Turns[0].Question.Should().Be("вопрос1");
        state.Turns[0].Answer.Should().Be("ответ1");
        state.Turns[1].Question.Should().Be("вопрос2");
        state.Turns[1].Answer.Should().Be("ответ2");
    }

    [Fact]
    public async Task Recovery_trims_to_MaxAdviceConversationTurns_when_more_than_5_pairs()
    {
        var userId = Guid.NewGuid();
        var events = new List<object>();
        for (var i = 1; i <= 7; i++)
        {
            var corrId = Guid.NewGuid();
            var t = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(60 - i * 5);
            events.Add(new ConsultationRequested(userId, corrId, "p", AdvisorTickType.OnDemand,
                t, UserQuestion: $"q{i}"));
            events.Add(new ConsultationAnswered(userId, corrId, $"a{i}",
                ConsultationSource.Claude, t.AddMinutes(1)));
        }

        await SeedEventsAsync($"user-{userId:N}-advice", events);

        var actor = Sys.ActorOf(UserAdviceActor.CreateProps(userId));
        actor.Tell(new GetAdviceConversation(userId));
        var state = ExpectMsg<AdviceConversationState>(TimeSpan.FromSeconds(5));

        state.Turns.Should().HaveCount(5);
        state.Turns[0].Question.Should().Be("q3");
        state.Turns[4].Question.Should().Be("q7");
    }

    [Fact]
    public async Task Recovery_skips_events_with_null_UserQuestion()
    {
        var userId = Guid.NewGuid();
        var corrId = Guid.NewGuid();

        await SeedEventsAsync($"user-{userId:N}-advice", new object[]
        {
            new ConsultationRequested(userId, corrId, "p", AdvisorTickType.OnDemand,
                DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10), UserQuestion: null),
            new ConsultationAnswered(userId, corrId, "ответ",
                ConsultationSource.Claude, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(9)),
        });

        var actor = Sys.ActorOf(UserAdviceActor.CreateProps(userId));
        actor.Tell(new GetAdviceConversation(userId));
        var state = ExpectMsg<AdviceConversationState>(TimeSpan.FromSeconds(5));

        state.Turns.Should().BeEmpty();
    }

    [Fact]
    public async Task Recovery_clears_conversation_when_last_interaction_older_than_ConversationTtl()
    {
        var userId = Guid.NewGuid();
        var corrId = Guid.NewGuid();
        var oldTime = DateTimeOffset.UtcNow - TimeSpan.FromHours(2);

        await SeedEventsAsync($"user-{userId:N}-advice", new object[]
        {
            new ConsultationRequested(userId, corrId, "p", AdvisorTickType.OnDemand,
                oldTime, UserQuestion: "вопрос"),
            new ConsultationAnswered(userId, corrId, "ответ",
                ConsultationSource.Claude, oldTime),
        });

        var actor = Sys.ActorOf(UserAdviceActor.CreateProps(userId));
        actor.Tell(new GetAdviceConversation(userId));
        var state = ExpectMsg<AdviceConversationState>(TimeSpan.FromSeconds(5));

        state.Turns.Should().BeEmpty();
    }

    [Fact]
    public async Task ParkThenResume_RequestsSnapshotFromParent_AndUsesLastKnownChatId()
    {
        var userId = Guid.NewGuid();

        await SeedEventsAsync($"user-{userId:N}-advice", new object[]
        {
            new AdviceParked(userId, AdvisorTickType.Weekly, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10)),
        });

        var parentProbe = CreateTestProbe();
        var actor = Sys.ActorOf(
            Props.Create(() => new UserAdviceActor(userId, parentProbe.Ref)),
            $"advice-resume-{userId:N}");

        actor.Tell(new ClaudeBecameAvailable(DateTimeOffset.UtcNow));

        var snapshotRequest = parentProbe.ExpectMsg<GetUserSnapshot>(TimeSpan.FromSeconds(5));
        snapshotRequest.UserId.Should().Be(userId);

        var emptySettings = new Dictionary<string, string?>().AsReadOnly() as IReadOnlyDictionary<string, string?>;
        parentProbe.Reply(new UserSnapshot(userId, IsRegistered: true, TelegramId: 9999L,
            Timezone: null, Settings: emptySettings!, LastKnownChatId: 42L));

        // Second ClaudeBecameAvailable must be ignored — flag cleared after first resume
        actor.Tell(new ClaudeBecameAvailable(DateTimeOffset.UtcNow));
        parentProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    private async Task SeedEventsAsync(string persistenceId, IEnumerable<object> events)
    {
        var eventList = events.ToList();
        var seeder = Sys.ActorOf(Props.Create(() => new EventSeeder(persistenceId, eventList, TestActor)));
        seeder.Tell(EventSeeder.Go.Instance);
        await ExpectMsgAsync<EventSeeder.Done>(TimeSpan.FromSeconds(5));
        await seeder.GracefulStop(TimeSpan.FromSeconds(3));
    }

    private sealed class EventSeeder : ReceivePersistentActor
    {
        public sealed class Go { public static readonly Go Instance = new(); }
        public sealed class Done { public static readonly Done Instance = new(); }

        private readonly List<object> _events;
        private readonly IActorRef _notify;

        public override string PersistenceId { get; }

        public EventSeeder(string persistenceId, List<object> events, IActorRef notify)
        {
            PersistenceId = persistenceId;
            _events = events;
            _notify = notify;

            Recover<object>(_ => { });

            Command<Go>(_ =>
            {
                var persisted = 0;
                PersistAll(_events, _ =>
                {
                    persisted++;
                    if (persisted == _events.Count)
                        _notify.Tell(Done.Instance);
                });
            });
        }
    }
}
