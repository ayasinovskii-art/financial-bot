using Akka.Actor;
using FinanceBot.Application.Actors.Categorizer;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class CategorizerActorTests : AkkaPersistenceTestBase
{
    [Fact]
    public void Match_returns_Rules_source_with_matched_category()
    {
        var actor = Sys.ActorOf(CategorizerActor.CreateProps(new StubRules(Category.Health)));
        var corrId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        actor.Tell(new CategorizeRequest(corrId, userId, expenseId, NormalizedDescription.FromRaw("аптека")));
        var resp = ExpectMsg<CategorizeResponse>();

        resp.CorrelationId.Should().Be(corrId);
        resp.Category.Should().Be(Category.Health);
        resp.Source.Should().Be(ExpenseSource.Rules);
        resp.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public void Miss_returns_Other_with_Fallback_source_and_NeedsReview()
    {
        var actor = Sys.ActorOf(CategorizerActor.CreateProps(new StubRules(null)));
        actor.Tell(new CategorizeRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            NormalizedDescription.FromRaw("неизвестная трата")));
        var resp = ExpectMsg<CategorizeResponse>();

        resp.Category.Should().Be(Category.Other);
        resp.Source.Should().Be(ExpenseSource.Fallback);
        resp.NeedsReview.Should().BeTrue();
    }

    private sealed class StubRules(Category? category) : ICategoryRules
    {
        public Category? Match(NormalizedDescription description) => category;
    }
}
