using Akka.Actor;
using FinanceBot.Application.Actors.Claude;
using FinanceBot.Application.Tests.Actors;
using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceBot.Application.Tests.Claude;

public sealed class ClaudeConsultantActorTests : AkkaPersistenceTestBase
{
    [Fact]
    public void Successful_response_is_propagated()
    {
        var stub = new StubClaudeClient(_ => new ClaudeResponse(
            CorrelationId: Guid.Empty,
            Content: "DiningOut",
            IsSuccess: true,
            LatencyMs: 42,
            InputTokens: 10, OutputTokens: 1,
            TokensRemaining: 1000, TokensResetAt: null,
            FailureReason: null, FailureMessage: null));

        var actor = Sys.ActorOf(ClaudeConsultantActor.CreateProps(stub, Options.Create(new ClaudeConsultantOptions())));

        var corr = Guid.NewGuid();
        actor.Tell(new ClaudeAskMessage(new ClaudeRequest(
            ClaudeUseCase.Categorization, "system", "обед", 16, corr)));

        var ok = ExpectMsg<ClaudeOkReply>();
        ok.Content.Should().Be("DiningOut");
    }

    [Fact]
    public void RateLimited_marks_unavailable_until_reset()
    {
        var resetAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var stub = new StubClaudeClient(_ => new ClaudeResponse(
            Guid.Empty, null, false, 1, null, null, 0, resetAt,
            FailureReason: ClaudeUnavailabilityReason.RateLimited,
            FailureMessage: "429"));

        var actor = Sys.ActorOf(ClaudeConsultantActor.CreateProps(stub, Options.Create(new ClaudeConsultantOptions())));

        actor.Tell(new ClaudeAskMessage(new ClaudeRequest(
            ClaudeUseCase.Categorization, "s", "u", 16, Guid.NewGuid())));
        var first = ExpectMsg<ClaudeUnavailableReply>();
        first.Reason.Should().Be(ClaudeUnavailabilityReason.RateLimited);

        // Второй запрос должен сразу прийти с unavailable, не дёргая клиента.
        var callsBefore = stub.CallCount;
        actor.Tell(new ClaudeAskMessage(new ClaudeRequest(
            ClaudeUseCase.Categorization, "s", "u", 16, Guid.NewGuid())));
        ExpectMsg<ClaudeUnavailableReply>();
        stub.CallCount.Should().Be(callsBefore);
    }

    [Fact]
    public void TransientError_uses_TransientUnavailableUntilHour()
    {
        var stub = new StubClaudeClient(_ => new ClaudeResponse(
            Guid.Empty, null, false, 1, null, null, null, null,
            FailureReason: ClaudeUnavailabilityReason.TransientError,
            FailureMessage: "503"));

        var opts = Options.Create(new ClaudeConsultantOptions { TransientUnavailableUntilHour = 20 });
        var actor = Sys.ActorOf(ClaudeConsultantActor.CreateProps(stub, opts));

        actor.Tell(new ClaudeAskMessage(new ClaudeRequest(
            ClaudeUseCase.Categorization, "s", "u", 16, Guid.NewGuid())));
        var reply = ExpectMsg<ClaudeUnavailableReply>();
        reply.Reason.Should().Be(ClaudeUnavailabilityReason.TransientError);
        reply.UnavailableUntil.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ResetUnavailable_returns_to_Available()
    {
        var stub = new StubClaudeClient(_ => new ClaudeResponse(
            Guid.Empty, null, false, 1, null, null, null, null,
            ClaudeUnavailabilityReason.TransientError, "503"));

        var actor = Sys.ActorOf(ClaudeConsultantActor.CreateProps(stub, Options.Create(new ClaudeConsultantOptions())));

        actor.Tell(new ClaudeAskMessage(new ClaudeRequest(
            ClaudeUseCase.Categorization, "s", "u", 16, Guid.NewGuid())));
        ExpectMsg<ClaudeUnavailableReply>();

        // Reset → следующий запрос снова проходит в клиента.
        actor.Tell(new ResetUnavailable());
        stub.NextResponse = _ => new ClaudeResponse(
            Guid.Empty, "OK", true, 1, null, null, null, null, null, null);

        actor.Tell(new ClaudeAskMessage(new ClaudeRequest(
            ClaudeUseCase.Categorization, "s", "u", 16, Guid.NewGuid())));
        ExpectMsg<ClaudeOkReply>();
    }

    private sealed class StubClaudeClient : IClaudeClient
    {
        public Func<ClaudeRequest, ClaudeResponse> NextResponse;
        public int CallCount { get; private set; }

        public StubClaudeClient(Func<ClaudeRequest, ClaudeResponse> nextResponse)
        {
            NextResponse = nextResponse;
        }

        public Task<ClaudeResponse> SendAsync(ClaudeRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(NextResponse(request) with { CorrelationId = request.CorrelationId });
        }
    }
}
