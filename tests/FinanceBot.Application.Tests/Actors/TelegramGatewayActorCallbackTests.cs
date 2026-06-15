using Akka.Actor;
using Akka.TestKit.Xunit2;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.Telegram.Commands;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class TelegramGatewayActorCallbackTests : TestKit
{
    private IActorRef CreateGateway(params ITelegramCallbackHandler[] callbackHandlers) =>
        Sys.ActorOf(TelegramGatewayActor.CreateProps(
            Options.Create(new UserDefaultsOptions()),
            [],
            callbackHandlers));

    private static IncomingCallbackQuery MakeCallback(string data) =>
        new(UpdateId: 1, CallbackQueryId: "cq1", ChatId: 1000, TelegramId: 42,
            Username: null, FirstName: null, Data: data, SentAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Callback_with_matching_prefix_dispatches_to_handler()
    {
        var probe = CreateTestProbe();
        var gateway = CreateGateway(new StubCallbackHandler("test:", probe));

        gateway.Tell(MakeCallback("test:abc"));

        probe.ExpectMsg<string>(msg => msg == "dispatched", TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Callback_with_unknown_prefix_acks_with_null()
    {
        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe, typeof(OutgoingCallbackAck));
        var gateway = CreateGateway();

        gateway.Tell(MakeCallback("unknown:xyz"));

        var ack = probe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(3));
        Assert.Null(ack.Text);
    }

    private sealed class StubCallbackHandler : ITelegramCallbackHandler
    {
        private readonly IActorRef _probe;

        public StubCallbackHandler(string prefix, IActorRef probe)
        {
            DataPrefix = prefix;
            _probe = probe;
        }

        public string DataPrefix { get; }

        public void Execute(TelegramCallbackContext ctx) => _probe.Tell("dispatched");
    }
}
