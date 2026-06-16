using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Claude;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.AccessControl;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class TelegramGatewayActorStage6Tests : TestKit
{
    private const long TelegramId = 7777L;
    private const long ChatId = 8888L;

    private static IncomingTelegramUpdate MakeUpdate(string text) =>
        new(UpdateId: 1, ChatId: ChatId, TelegramId: TelegramId,
            Username: null, FirstName: null, LastName: null,
            Text: text, SentAt: DateTimeOffset.UtcNow);

    private (IActorRef gateway, TestProbe accessProbe, TestProbe consultantProbe, TestProbe shardProbe) SetupGateway()
    {
        var accessProbe = CreateTestProbe();
        var consultantProbe = CreateTestProbe();
        var shardProbe = CreateTestProbe();

        var registry = Akka.Hosting.ActorRegistry.For(Sys);
        registry.Register<AccessControlSingletonMarker>(accessProbe.Ref);
        registry.Register<ClaudeConsultantSingletonMarker>(consultantProbe.Ref);
        registry.Register<UserShardMarker>(shardProbe.Ref);

        var gateway = Sys.ActorOf(TelegramGatewayActor.CreateProps(
            Options.Create(new UserDefaultsOptions()),
            [],
            [],
            new NlpPendingCache()));

        return (gateway, accessProbe, consultantProbe, shardProbe);
    }

    private void AllowAccess(TestProbe accessProbe)
    {
        accessProbe.ExpectMsg<IsAllowed>(TimeSpan.FromSeconds(3));
        accessProbe.Reply(new AccessDecision.Allowed(TelegramId, AccessRole.User));
    }

    [Fact]
    public void Text_with_amount_high_confidence_dispatches_ReportExpense_to_shard()
    {
        var (gateway, accessProbe, consultantProbe, shardProbe) = SetupGateway();

        gateway.Tell(MakeUpdate("пообедал на 700"));
        AllowAccess(accessProbe);

        var askMsg = consultantProbe.ExpectMsg<ClaudeAskMessage>(TimeSpan.FromSeconds(3));

        const string json = """{"type":"expense","amount":700.00,"category":"DiningOut","description":"обед","confidence":0.95,"isFinancial":true}""";
        gateway.Tell(new ClaudeOkReply(askMsg.Request.CorrelationId, json, 10, null, null));

        var envelope = (ShardEnvelope)shardProbe.FishForMessage(
            m => m is ShardEnvelope se && se.Message is ReportExpense,
            TimeSpan.FromSeconds(3));
        Assert.IsType<ReportExpense>(envelope.Message);
    }

    [Fact]
    public void Low_confidence_isFinancial_true_publishes_OutgoingInlineKeyboard()
    {
        var (gateway, accessProbe, consultantProbe, _) = SetupGateway();
        var eventProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(eventProbe, typeof(OutgoingInlineKeyboard));

        gateway.Tell(MakeUpdate("пообедал на 700"));
        AllowAccess(accessProbe);

        var askMsg = consultantProbe.ExpectMsg<ClaudeAskMessage>(TimeSpan.FromSeconds(3));

        const string json = """{"type":"expense","amount":700.00,"category":"Other","description":"что-то","confidence":0.5,"isFinancial":true}""";
        gateway.Tell(new ClaudeOkReply(askMsg.Request.CorrelationId, json, 10, null, null));

        var kb = eventProbe.ExpectMsg<OutgoingInlineKeyboard>(TimeSpan.FromSeconds(3));
        Assert.Equal(ChatId, kb.ChatId);
    }

    [Fact]
    public void IsFinancial_false_sends_no_ReportExpense_to_shard()
    {
        var (gateway, accessProbe, consultantProbe, shardProbe) = SetupGateway();

        gateway.Tell(MakeUpdate("пообедал на 700"));
        AllowAccess(accessProbe);

        var askMsg = consultantProbe.ExpectMsg<ClaudeAskMessage>(TimeSpan.FromSeconds(3));

        // Consume the LinkUserChat that LinkChat() sent before the Claude Ask
        shardProbe.ExpectMsg<ShardEnvelope>(
            se => se.Message is LinkUserChat,
            TimeSpan.FromSeconds(3));

        const string json = """{"type":"expense","amount":0,"category":"Other","description":"","confidence":0.1,"isFinancial":false}""";
        gateway.Tell(new ClaudeOkReply(askMsg.Request.CorrelationId, json, 10, null, null));

        shardProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void ClaudeUnavailableReply_triggers_fallback_AmountTextParser_dispatches_ReportExpense()
    {
        var (gateway, accessProbe, consultantProbe, shardProbe) = SetupGateway();

        gateway.Tell(MakeUpdate("пообедал на 700"));
        AllowAccess(accessProbe);

        var askMsg = consultantProbe.ExpectMsg<ClaudeAskMessage>(TimeSpan.FromSeconds(3));

        gateway.Tell(new ClaudeUnavailableReply(
            askMsg.Request.CorrelationId,
            ClaudeUnavailabilityReason.Overloaded,
            DateTimeOffset.UtcNow.AddSeconds(30)));

        var envelope = (ShardEnvelope)shardProbe.FishForMessage(
            m => m is ShardEnvelope se && se.Message is ReportExpense,
            TimeSpan.FromSeconds(3));
        Assert.IsType<ReportExpense>(envelope.Message);
    }
}
