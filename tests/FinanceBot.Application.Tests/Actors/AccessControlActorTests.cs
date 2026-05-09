using Akka.Actor;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Domain.Commands.AccessControl;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class AccessControlActorTests : AkkaPersistenceTestBase
{
    private const long Admin = 100;
    private const long Other = 200;

    private IActorRef CreateActor() => Sys.ActorOf(AccessControlActor.CreateProps(
        Options.Create(new AuthOptions { AdminUserIds = [Admin] })));

    [Fact]
    public void Admin_is_allowed_without_explicit_whitelist()
    {
        var actor = CreateActor();
        actor.Tell(new IsAllowed(Admin));
        var decision = ExpectMsg<AccessDecision.Allowed>();
        decision.Role.Should().Be(AccessRole.Admin);
    }

    [Fact]
    public void Non_admin_is_denied_by_default()
    {
        var actor = CreateActor();
        actor.Tell(new IsAllowed(Other));
        ExpectMsg<AccessDecision.Denied>();
    }

    [Fact]
    public void WhitelistUser_by_admin_succeeds_and_allows_access()
    {
        var actor = CreateActor();

        actor.Tell(new WhitelistUser(Admin, Other));
        var added = ExpectMsg<AccessControlReply.Whitelisted>();
        added.TelegramId.Should().Be(Other);

        actor.Tell(new IsAllowed(Other));
        ExpectMsg<AccessDecision.Allowed>();
    }

    [Fact]
    public void WhitelistUser_by_non_admin_is_rejected()
    {
        var actor = CreateActor();
        actor.Tell(new WhitelistUser(Other, 999));
        ExpectMsg<AccessControlReply.NotAdmin>();
    }

    [Fact]
    public void RevokeUser_disables_access()
    {
        var actor = CreateActor();
        actor.Tell(new WhitelistUser(Admin, Other));
        ExpectMsg<AccessControlReply.Whitelisted>();

        actor.Tell(new RevokeUser(Admin, Other));
        ExpectMsg<AccessControlReply.Revoked>();

        actor.Tell(new IsAllowed(Other));
        ExpectMsg<AccessDecision.Denied>();
    }

    [Fact]
    public void Whitelisting_already_whitelisted_returns_AlreadyWhitelisted()
    {
        var actor = CreateActor();
        actor.Tell(new WhitelistUser(Admin, Other));
        ExpectMsg<AccessControlReply.Whitelisted>();
        actor.Tell(new WhitelistUser(Admin, Other));
        ExpectMsg<AccessControlReply.AlreadyWhitelisted>();
    }

    [Fact]
    public void ListWhitelisted_returns_active_entries()
    {
        var actor = CreateActor();
        actor.Tell(new WhitelistUser(Admin, 300));
        ExpectMsg<AccessControlReply.Whitelisted>();
        actor.Tell(new WhitelistUser(Admin, 400));
        ExpectMsg<AccessControlReply.Whitelisted>();

        actor.Tell(new ListWhitelisted());
        var list = ExpectMsg<AccessControlReply.WhitelistList>();
        list.Entries.Should().HaveCount(2);
        list.Admins.Should().Contain(Admin);
    }
}
