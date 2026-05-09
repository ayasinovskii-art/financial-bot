using Akka.Actor;
using FinanceBot.Application.Actors.UserTemplates;
using FinanceBot.Application.Actors.UserTemplates.Messages;
using FinanceBot.Domain.Commands.Templates;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class UserTemplatesActorTests : AkkaPersistenceTestBase
{
    [Fact]
    public void Add_template_succeeds()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserTemplatesActor.CreateProps(userId));

        actor.Tell(new AddTemplate(userId, "lunch", 700m, WeekdaysSchedule.Instance, Category.DiningOut));
        var added = ExpectMsg<TemplateAdded>();
        added.Template.Name.Should().Be("lunch");
        added.Template.Category.Should().Be(Category.DiningOut);
    }

    [Fact]
    public void Add_duplicate_name_rejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserTemplatesActor.CreateProps(userId));

        actor.Tell(new AddTemplate(userId, "lunch", 700m, WeekdaysSchedule.Instance, null));
        ExpectMsg<TemplateAdded>();
        actor.Tell(new AddTemplate(userId, "lunch", 800m, DailySchedule.Instance, null));
        ExpectMsg<TemplateRejected>();
    }

    [Fact]
    public void List_returns_added_templates()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserTemplatesActor.CreateProps(userId));

        actor.Tell(new AddTemplate(userId, "lunch", 700m, WeekdaysSchedule.Instance, null));
        ExpectMsg<TemplateAdded>();
        actor.Tell(new AddTemplate(userId, "rent", 30000m, new DaysOfMonthSchedule([1]), null));
        ExpectMsg<TemplateAdded>();

        actor.Tell(new ListTemplates(userId));
        var list = ExpectMsg<TemplateList>();
        list.Templates.Should().HaveCount(2);
    }

    [Fact]
    public void Remove_unknown_rejected()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserTemplatesActor.CreateProps(userId));

        actor.Tell(new RemoveTemplate(userId, "nonexistent"));
        ExpectMsg<TemplateRejected>();
    }

    [Fact]
    public void Remove_existing_succeeds()
    {
        var userId = Guid.NewGuid();
        var actor = Sys.ActorOf(UserTemplatesActor.CreateProps(userId));

        actor.Tell(new AddTemplate(userId, "lunch", 700m, WeekdaysSchedule.Instance, null));
        ExpectMsg<TemplateAdded>();

        actor.Tell(new RemoveTemplate(userId, "lunch"));
        ExpectMsg<TemplateRemoved>();

        actor.Tell(new ListTemplates(userId));
        ExpectMsg<TemplateList>().Templates.Should().BeEmpty();
    }
}
