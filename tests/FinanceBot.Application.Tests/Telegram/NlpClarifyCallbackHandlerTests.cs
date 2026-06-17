using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Xunit2;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.Telegram.Commands;
using FinanceBot.Application.Actors.Telegram.Commands.Handlers;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Csv;
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
            cache,
            new ImportPendingCache(),
            new StubTelegramBot(),
            new StubCsvImportParser()));
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

    [Fact]
    public void Cache_has_income_entry_shortArg_y_dispatches_ReportIncome_and_acks_recorded()
    {
        var shardProbe = CreateTestProbe();
        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(shardProbe.Ref);

        var (gateway, cache) = CreateGateway();
        var eventProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(eventProbe, typeof(OutgoingCallbackAck));

        var userId = Guid.NewGuid();
        var confirmationId = Guid.NewGuid();
        cache.Add(confirmationId, new NlpPendingEntry(ChatId, userId, null,
            new NlpParseResult("income", 50000m, "Other", "зарплата", 0.9, true),
            DateTimeOffset.UtcNow));

        gateway.Tell(MakeCallback(CallbackPayload.Encode("nlpc", confirmationId, "y")));

        var envelope = shardProbe.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(3));
        Assert.IsType<ReportIncome>(envelope.Message);

        shardProbe.Reply(new IncomeAccepted(userId, Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow), 50000m, 25000m, 12500m, 12500m));

        var ack = eventProbe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(5));
        Assert.Equal("Записано.", ack.Text);
    }

    [Fact]
    public void Invalid_payload_acks_not_understood()
    {
        var (gateway, _) = CreateGateway();
        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe, typeof(OutgoingCallbackAck));

        gateway.Tell(MakeCallback("nlpc:not-valid-guid"));

        var ack = probe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(3));
        Assert.Equal("Не понял callback.", ack.Text);
    }

    [Fact]
    public void Cache_has_entry_unknown_shortArg_acks_not_understood()
    {
        var (gateway, cache) = CreateGateway();
        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe, typeof(OutgoingCallbackAck));

        var confirmationId = Guid.NewGuid();
        cache.Add(confirmationId, new NlpPendingEntry(ChatId, Guid.NewGuid(), null,
            new NlpParseResult("expense", 700m, "DiningOut", "обед", 0.7, true),
            DateTimeOffset.UtcNow));

        gateway.Tell(MakeCallback(CallbackPayload.Encode("nlpc", confirmationId, "x")));

        var ack = probe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(3));
        Assert.Equal("Не понял ответ.", ack.Text);
    }

    [Fact]
    public void ShardMarker_not_registered_shortArg_y_acks_internal_error()
    {
        var (gateway, cache) = CreateGateway();
        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe, typeof(OutgoingCallbackAck));

        var confirmationId = Guid.NewGuid();
        cache.Add(confirmationId, new NlpPendingEntry(ChatId, Guid.NewGuid(), null,
            new NlpParseResult("expense", 700m, "DiningOut", "обед", 0.9, true),
            DateTimeOffset.UtcNow));

        gateway.Tell(MakeCallback(CallbackPayload.Encode("nlpc", confirmationId, "y")));

        var ack = probe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(3));
        Assert.Equal("Внутренняя ошибка.", ack.Text);
    }

    [Fact]
    public void Shard_ask_timeout_acks_internal_error()
    {
        // Use a deaf shard that never replies so the Ask times out,
        // exercising the !t.IsCompletedSuccessfully branch (lines 91-93).
        // Call Execute directly with a short AskTimeout so the test completes quickly.
        var deaf = Sys.ActorOf(Props.Create<DeafShardActor>());
        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(deaf);

        var cache = new NlpPendingCache();
        var handler = new NlpClarifyCallbackHandler(cache);
        var selfProbe = CreateTestProbe();

        var userId = Guid.NewGuid();
        var confirmationId = Guid.NewGuid();
        cache.Add(confirmationId, new NlpPendingEntry(ChatId, userId, null,
            new NlpParseResult("expense", 700m, "DiningOut", "обед", 0.9, true),
            DateTimeOffset.UtcNow));

        handler.Execute(new TelegramCallbackContext
        {
            Callback = MakeCallback(CallbackPayload.Encode("nlpc", confirmationId, "y")),
            Self = selfProbe.Ref,
            System = Sys,
            Log = Logging.GetLogger(Sys, nameof(NlpClarifyCallbackHandlerTests)),
            AskTimeout = TimeSpan.FromMilliseconds(200)
        });

        var completed = selfProbe.ExpectMsg<TelegramCommandCompleted>(TimeSpan.FromSeconds(3));
        var ack = completed.Outgoing.OfType<OutgoingCallbackAck>().Single();
        Assert.Equal("Внутренняя ошибка.", ack.Text);
    }

    private sealed class DeafShardActor : ReceiveActor
    {
        public DeafShardActor() => ReceiveAny(_ => { });
    }

    private sealed class StubTelegramBot : ITelegramBot
    {
        public Task SendTextAsync(long chatId, string text, CancellationToken ct) => Task.CompletedTask;
        public Task SendInlineKeyboardAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> rows, CancellationToken ct) => Task.CompletedTask;
        public Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken ct) => Task.CompletedTask;
        public Task SendPhotoAsync(long chatId, byte[] photo, string fileName, string? caption, CancellationToken ct) => Task.CompletedTask;
        public Task SendDocumentAsync(long chatId, byte[] document, string fileName, string? caption, CancellationToken ct) => Task.CompletedTask;
        public Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct) => Task.FromResult(Array.Empty<byte>());
        public Task<TelegramPollResult> PollAsync(long offset, TimeSpan timeout, CancellationToken ct) => Task.FromResult(new TelegramPollResult([], [], offset));
        public Task<bool> SetWebhookAsync(string url, CancellationToken ct) => Task.FromResult(true);
        public Task DeleteWebhookAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubCsvImportParser : ICsvImportParser
    {
        public CsvParseResult Parse(string csvText) => new([], 0);
    }
}
