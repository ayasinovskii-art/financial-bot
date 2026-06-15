using Akka.Actor;
using FinanceBot.Application.Actors.Advisor;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Events.Advisor;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class AdvisorActorGoalTests : AkkaPersistenceTestBase
{
    private readonly StubSnapshotReader _reader = new();

    [Fact]
    public void FetchActiveGoals_shard_not_registered_returns_empty_ActiveGoals()
    {
        // No UserShardMarker registered — FetchActiveGoalsAsync exits early with empty list.
        var advisor = Sys.ActorOf(AdvisorActor.CreateProps(_reader));
        var corrId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        advisor.Tell(new BuildSnapshotRequest(corrId, userId));

        var response = ExpectMsg<BuildSnapshotResponse>(TimeSpan.FromSeconds(5));
        response.Snapshot.Should().NotBeNull();
        response.Snapshot!.ActiveGoals.Should().BeEmpty();
    }

    [Fact]
    public void FetchActiveGoals_success_path_populates_ActiveGoals_from_shard()
    {
        var shard = CreateTestProbe();
        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(shard.Ref);

        var advisor = Sys.ActorOf(AdvisorActor.CreateProps(_reader));
        var corrId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        advisor.Tell(new BuildSnapshotRequest(corrId, userId));

        // Shard receives GetUserGoals via ShardEnvelope
        var envelope = shard.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(3));
        envelope.Message.Should().BeOfType<GetUserGoals>();

        var goalId = Guid.NewGuid();
        shard.Reply(new UserGoalsList([
            new GoalState(goalId, "Скальная школа в горах", TargetAmount: 80_000m, null, IsCompleted: false),
            new GoalState(Guid.NewGuid(), "Завершённая цель", null, null, IsCompleted: true)
        ]));

        var response = ExpectMsg<BuildSnapshotResponse>(TimeSpan.FromSeconds(3));
        response.Snapshot.Should().NotBeNull();
        response.Snapshot!.ActiveGoals.Should().HaveCount(1);
        response.Snapshot.ActiveGoals[0].GoalId.Should().Be(goalId);
        response.Snapshot.ActiveGoals[0].Description.Should().Be("Скальная школа в горах");
        response.Snapshot.ActiveGoals[0].TargetAmount.Should().Be(80_000m);
    }

    [Fact]
    public void FetchActiveGoals_timeout_returns_empty_ActiveGoals_without_exception()
    {
        // Register a "deaf" actor that never replies — triggers AskTimeoutException in FetchActiveGoalsAsync.
        var deaf = Sys.ActorOf(Props.Create<DeafActor>(), "deaf-shard");
        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(deaf);

        var advisor = Sys.ActorOf(AdvisorActor.CreateProps(_reader));
        var corrId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        advisor.Tell(new BuildSnapshotRequest(corrId, userId));

        // Wait longer than the 3s Ask timeout inside FetchActiveGoalsAsync.
        var response = ExpectMsg<BuildSnapshotResponse>(TimeSpan.FromSeconds(6));
        response.ErrorMessage.Should().BeNull("snapshot build itself must succeed even when goal fetch times out");
        response.Snapshot.Should().NotBeNull();
        response.Snapshot!.ActiveGoals.Should().BeEmpty();
    }

    [Fact]
    public void FetchActiveGoals_unexpected_reply_type_returns_empty_ActiveGoals()
    {
        var shard = CreateTestProbe();
        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(shard.Ref);

        var advisor = Sys.ActorOf(AdvisorActor.CreateProps(_reader));
        var corrId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        advisor.Tell(new BuildSnapshotRequest(corrId, userId));

        shard.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(3));
        shard.Reply("unexpected");

        var response = ExpectMsg<BuildSnapshotResponse>(TimeSpan.FromSeconds(3));
        response.Snapshot!.ActiveGoals.Should().BeEmpty();
    }

    private sealed class StubSnapshotReader : IAdvisorSnapshotReader
    {
        public Task<AdvisorSnapshot> BuildAsync(Guid userId, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(new AdvisorSnapshot(
                UserId: userId,
                BuiltAt: now,
                CurrentPeriod: null,
                PreviousPeriod: null,
                CurrentByCategory: Array.Empty<CategorySnapshot>(),
                PreviousByCategory: Array.Empty<CategorySnapshot>(),
                TopExpenses: Array.Empty<TopExpense>(),
                DaysToEndOfPeriod: null,
                Settings: new Dictionary<string, string>()));
    }

    private sealed class DeafActor : ReceiveActor
    {
        public DeafActor() => ReceiveAny(_ => { });
    }
}
