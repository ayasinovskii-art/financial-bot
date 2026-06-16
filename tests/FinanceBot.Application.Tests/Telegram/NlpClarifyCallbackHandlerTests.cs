using Akka.Actor;
using Akka.TestKit.Xunit2;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.Telegram.Commands.Handlers;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceBot.Application.Tests.Telegram;

public sealed class NlpClarifyCallbackHandlerTests : TestKit
{
    private const long ChatId = 1000L;

    private (IActorRef gateway, NlpPendingCache cache) CreateGateway()
    {
        var cache = new NlpPendingCache();
        var handler = new NlpClarifyCallbackHandler(cache);
        var gateway = Sys.ActorOf(TelegramGatewayActor.CreateProps(
            Options.Create(new UserDefaultsOptions()),
            [],
            [handler],
            cache));
        return (gateway, cache);
    }

    private static IncomingCallbackQuery MakeCallback(string data) =>
        new(UpdateId: 1, CallbackQueryId: "cq1", ChatId: ChatId,
            TelegramId: 42L, Username: null, FirstName: null, Data: data,
            SentAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Cache_empty_acks_session_expired()
    {
        var (gateway, _) = CreateGateway();
        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe, typeof(OutgoingCallbackAck));

        gateway.Tell(MakeCallback(CallbackPayload.Encode("nlpc", Guid.NewGuid(), "y")));

        var ack = probe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(3));
        Assert.Equal("Сессия устарела, введите заново.", ack.Text);
    }

    [Fact]
    public void Cache_has_entry_shortArg_n_acks_cancelled()
    {
        var (gateway, cache) = CreateGateway();
        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe, typeof(OutgoingCallbackAck));

        var confirmationId = Guid.NewGuid();
        cache.Add(confirmationId, new NlpPendingEntry(ChatId, Guid.NewGuid(), null,
            new NlpParseResult("expense", 700m, "DiningOut", "обед", 0.7, true),
            DateTimeOffset.UtcNow));

        gateway.Tell(MakeCallback(CallbackPayload.Encode("nlpc", confirmationId, "n")));

        var ack = probe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(3));
        Assert.Equal("Отменено.", ack.Text);
    }

    [Fact]
    public void Cache_has_expense_entry_shortArg_y_dispatches_ReportExpense_and_acks_recorded()
    {
        var shardProbe = CreateTestProbe();
        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(shardProbe.Ref);

        var (gateway, cache) = CreateGateway();
        var eventProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(eventProbe, typeof(OutgoingCallbackAck));

        var userId = Guid.NewGuid();
        var confirmationId = Guid.NewGuid();
        cache.Add(confirmationId, new NlpPendingEntry(ChatId, userId, null,
            new NlpParseResult("expense", 700m, "DiningOut", "обед", 0.9, true),
            DateTimeOffset.UtcNow));

        gateway.Tell(MakeCallback(CallbackPayload.Encode("nlpc", confirmationId, "y")));

        var envelope = shardProbe.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(3));
        Assert.IsType<ReportExpense>(envelope.Message);

        shardProbe.Reply(new ExpenseAccepted(userId, Guid.NewGuid(), Guid.NewGuid(), 700m,
            Category.DiningOut, Bucket.Fun, 0m, 0m, 0m, 25000m, 12500m, 12500m));

        var ack = eventProbe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(5));
        Assert.Equal("Записано.", ack.Text);
    }
}
