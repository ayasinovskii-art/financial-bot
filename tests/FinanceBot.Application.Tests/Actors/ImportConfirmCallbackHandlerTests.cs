using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FinanceBot.Application.Actors.Telegram;
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

namespace FinanceBot.Application.Tests.Actors;

public sealed class ImportConfirmCallbackHandlerTests : TestKit
{
    private const long ChatId = 5000L;

    private (IActorRef gateway, ImportPendingCache cache, TestProbe shardProbe) Setup()
    {
        var shardProbe = CreateTestProbe();
        var cache = new ImportPendingCache();

        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(shardProbe.Ref);

        var handler = new ImportConfirmCallbackHandler(cache);
        var gateway = Sys.ActorOf(TelegramGatewayActor.CreateProps(
            Options.Create(new UserDefaultsOptions()),
            [],
            [handler],
            new NlpPendingCache(),
            cache,
            new StubTelegramBot(),
            new StubCsvImportParser()));

        return (gateway, cache, shardProbe);
    }

    private static IncomingCallbackQuery MakeCallback(string data) =>
        new(UpdateId: 1, CallbackQueryId: "cq1", ChatId: ChatId, TelegramId: 42L,
            Username: null, FirstName: null, Data: data, SentAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Confirm_y_dispatches_BulkAddExpenses_and_replies_success()
    {
        var (gateway, cache, shardProbe) = Setup();
        var ackProbe = CreateTestProbe();
        var replyProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(ackProbe, typeof(OutgoingCallbackAck));
        Sys.EventStream.Subscribe(replyProbe, typeof(OutgoingTelegramReply));

        var userId = Guid.NewGuid();
        var rows = new List<ParsedImportRow> { new(100m, new DateOnly(2026, 1, 1), "Кофе") };
        cache.Set(userId, new ImportPendingEntry(ChatId, userId, rows, DateTimeOffset.UtcNow));

        gateway.Tell(MakeCallback(CallbackPayload.Encode("csvimport", userId, "y")));

        var envelope = (ShardEnvelope)shardProbe.FishForMessage(
            m => m is ShardEnvelope se && se.Message is BulkAddExpenses,
            TimeSpan.FromSeconds(3));
        Assert.IsType<BulkAddExpenses>(envelope.Message);

        shardProbe.Reply(new BulkExpensesResult(userId, 1, 0));

        var ack = ackProbe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(5));
        Assert.Null(ack.Text);

        var reply = replyProbe.ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(5));
        Assert.Equal(ChatId, reply.ChatId);
        Assert.Equal(TelegramReplies.CsvImportSuccess(1, 0), reply.Text);
    }

    [Fact]
    public void Confirm_n_removes_cache_and_replies_cancelled()
    {
        var (gateway, cache, _) = Setup();
        var ackProbe = CreateTestProbe();
        var replyProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(ackProbe, typeof(OutgoingCallbackAck));
        Sys.EventStream.Subscribe(replyProbe, typeof(OutgoingTelegramReply));

        var userId = Guid.NewGuid();
        var rows = new List<ParsedImportRow> { new(200m, new DateOnly(2026, 1, 2), "Обед") };
        cache.Set(userId, new ImportPendingEntry(ChatId, userId, rows, DateTimeOffset.UtcNow));

        gateway.Tell(MakeCallback(CallbackPayload.Encode("csvimport", userId, "n")));

        var ack = ackProbe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(3));
        Assert.Null(ack.Text);

        var reply = replyProbe.ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        Assert.Equal(ChatId, reply.ChatId);
        Assert.Equal(TelegramReplies.CsvImportCancelled(), reply.Text);

        Assert.False(cache.TryGet(userId, out _));
    }

    [Fact]
    public void Stale_session_replies_session_expired()
    {
        var (gateway, _, _) = Setup();
        var ackProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(ackProbe, typeof(OutgoingCallbackAck));

        var userId = Guid.NewGuid(); // nothing in cache

        gateway.Tell(MakeCallback(CallbackPayload.Encode("csvimport", userId, "y")));

        var ack = ackProbe.ExpectMsg<OutgoingCallbackAck>(TimeSpan.FromSeconds(3));
        Assert.Equal(TelegramReplies.CsvImportSessionExpired(), ack.Text);
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
