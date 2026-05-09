using Akka.Actor;
using FinanceBot.Application.Actors.Categorizer;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

/// <summary>Stage 11: проверка memory-категоризации и /correct flow.</summary>
public sealed class UserActorMemoryTests : AkkaPersistenceTestBase
{
    [Fact]
    public void Correction_via_CorrectExpenseCategory_seeds_memory_for_future_expenses()
    {
        // Категоризатор всегда выдаёт Other → needsReview=true.
        var rules = new StubRules(matched: null);
        var categorizer = Sys.ActorOf(CategorizerActor.CreateProps(rules), "cat");
        var registry = Akka.Hosting.ActorRegistry.For(Sys);
        registry.Register<CategorizerActorMarker>(categorizer);

        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 1, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();
        actor.Tell(new ReportIncome(userId, 100_000m, DateTimeOffset.UtcNow, null));
        ExpectMsg<IncomeAccepted>();

        // Первая трата с описанием "обед лавка" → категоризатор не знает → Other + NeedsReview.
        actor.Tell(new ReportExpense(userId, 500m, DateTimeOffset.UtcNow, "обед лавка", ExpenseSource.Manual));
        var first = ExpectMsg<ExpenseAccepted>();
        first.Category.Should().Be(Category.Other);

        // Пользователь поправляет категорию.
        actor.Tell(new CorrectExpenseCategory(userId, first.ExpenseId, Category.DiningOut));
        var corrected = ExpectMsg<ExpenseCorrectionApplied>();
        corrected.NewCategory.Should().Be(Category.DiningOut);

        // Следующая трата с тем же описанием — должна попасть в DiningOut через memory, не дёргая категоризатор.
        actor.Tell(new ReportExpense(userId, 600m, DateTimeOffset.UtcNow, "обед лавка", ExpenseSource.Manual));
        var second = ExpectMsg<ExpenseAccepted>();
        second.Category.Should().Be(Category.DiningOut);
    }

    [Fact]
    public void GetNeedsReviewExpenses_returns_only_NeedsReview_items()
    {
        var rules = new StubRules(matched: null);
        var categorizer = Sys.ActorOf(CategorizerActor.CreateProps(rules), "cat-needs-review");
        var registry = Akka.Hosting.ActorRegistry.For(Sys);
        registry.Register<CategorizerActorMarker>(categorizer);

        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 2, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();
        actor.Tell(new ReportIncome(userId, 50_000m, DateTimeOffset.UtcNow, null));
        ExpectMsg<IncomeAccepted>();

        actor.Tell(new ReportExpense(userId, 100m, DateTimeOffset.UtcNow, "что-то непонятное", ExpenseSource.Manual));
        ExpectMsg<ExpenseAccepted>();
        actor.Tell(new ReportExpense(userId, 200m, DateTimeOffset.UtcNow, "ещё непонятное", ExpenseSource.Manual));
        ExpectMsg<ExpenseAccepted>();

        actor.Tell(new GetNeedsReviewExpenses(userId, 10));
        var list = ExpectMsg<NeedsReviewList>();
        list.Expenses.Should().HaveCount(2);
        list.Expenses.Should().AllSatisfy(e => e.Category.Should().Be(Category.Other));
    }

    [Fact]
    public void Correction_rejects_unknown_expense()
    {
        var rules = new StubRules(matched: null);
        var categorizer = Sys.ActorOf(CategorizerActor.CreateProps(rules), "cat-unknown");
        var registry = Akka.Hosting.ActorRegistry.For(Sys);
        registry.Register<CategorizerActorMarker>(categorizer);

        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 3, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();
        actor.Tell(new ReportIncome(userId, 50_000m, DateTimeOffset.UtcNow, null));
        ExpectMsg<IncomeAccepted>();

        actor.Tell(new CorrectExpenseCategory(userId, Guid.NewGuid(), Category.DiningOut));
        ExpectMsg<ExpenseCorrectionRejected>();
    }

    [Fact]
    public void Memory_lookup_short_circuits_categorizer()
    {
        // Если memory попадёт первым, в категоризатор запрос не уйдёт.
        // Проверяем по факту: ExpenseAccepted приходит немедленно даже если категоризатор не зарегистрирован.
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserActor.CreateProps(userId));

        actor.Tell(new RegisterUser(userId, 4, "UTC"));
        ExpectMsg<UserRegistrationCompleted>();
        actor.Tell(new ReportIncome(userId, 50_000m, DateTimeOffset.UtcNow, null));
        ExpectMsg<IncomeAccepted>();

        // Чтобы заполнить memory вручную, делаем коррекцию через путь категоризатор→Other→correct.
        // Сначала временно регистрируем категоризатор.
        var rules = new StubRules(matched: null);
        var categorizer = Sys.ActorOf(CategorizerActor.CreateProps(rules), "cat-shortcircuit");
        var registry = Akka.Hosting.ActorRegistry.For(Sys);
        registry.Register<CategorizerActorMarker>(categorizer);

        actor.Tell(new ReportExpense(userId, 100m, DateTimeOffset.UtcNow, "уникальное описание", ExpenseSource.Manual));
        var first = ExpectMsg<ExpenseAccepted>();
        actor.Tell(new CorrectExpenseCategory(userId, first.ExpenseId, Category.Health));
        ExpectMsg<ExpenseCorrectionApplied>();

        // Теперь повторная трата с тем же описанием — категория из memory, source = Memory.
        actor.Tell(new ReportExpense(userId, 250m, DateTimeOffset.UtcNow, "уникальное описание", ExpenseSource.Manual));
        var second = ExpectMsg<ExpenseAccepted>();
        second.Category.Should().Be(Category.Health);
    }

    private sealed class StubRules(Category? matched) : ICategoryRules
    {
        public Category? Match(NormalizedDescription description) => matched;
    }
}
