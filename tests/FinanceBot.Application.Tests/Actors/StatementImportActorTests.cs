using Akka.Actor;
using FinanceBot.Application.Actors.StatementImport;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class StatementImportActorTests : AkkaPersistenceTestBase
{
    private static IncomingTelegramFile Photo() =>
        new(1, ChatId: 100, TelegramId: 42, "u", "T", null, "file-1", FileKind.Photo, "image/jpeg", "выписка", DateTimeOffset.UtcNow);

    private static IncomingTelegramFile Document() =>
        new(2, ChatId: 100, TelegramId: 42, "u", "T", null, "file-2", FileKind.Document, "text/csv", null, DateTimeOffset.UtcNow);

    private IActorRef CreateActor(StatementExtractionResult extraction)
    {
        var bot = new StubTelegramBot(new TelegramFileDownload([1, 2, 3], "image/jpeg"));
        var extractor = new StubExtractor(extraction);
        return Sys.ActorOf(StatementImportActor.CreateProps(bot, extractor));
    }

    [Fact]
    public void Photo_extracts_proposes_to_shard_and_sends_inline_keyboard()
    {
        var shard = CreateTestProbe();
        Akka.Hosting.ActorRegistry.For(Sys).Register<UserShardMarker>(shard.Ref);

        var outbox = CreateTestProbe();
        Sys.EventStream.Subscribe(outbox.Ref, typeof(OutgoingTelegramReply));
        Sys.EventStream.Subscribe(outbox.Ref, typeof(OutgoingInlineKeyboard));

        var txns = new ImportedTransaction[]
        {
            new(new DateOnly(2026, 6, 5), 750m, "обед", TransactionKind.Expense)
        };
        var actor = CreateActor(StatementExtractionResult.Success(txns));

        actor.Tell(Photo());

        outbox.ExpectMsg<OutgoingTelegramReply>(r => r.Text.Should().Contain("Распознаю"));

        var envelope = shard.ExpectMsg<ShardEnvelope>(TimeSpan.FromSeconds(5));
        var propose = envelope.Message.Should().BeOfType<ProposeStatementImport>().Subject;
        propose.Transactions.Should().HaveCount(1);
        shard.Reply(new StatementImportProposed(propose.ProposalId, 1, 1, 0, 750m, 0m));

        outbox.ExpectMsg<OutgoingInlineKeyboard>(kb =>
        {
            kb.ChatId.Should().Be(100);
            kb.Rows.SelectMany(r => r).Should().Contain(b => b.CallbackData.StartsWith("import:confirm:"));
        });
    }

    [Fact]
    public void Document_is_not_supported_yet_and_replies_csv_hint()
    {
        var outbox = CreateTestProbe();
        Sys.EventStream.Subscribe(outbox.Ref, typeof(OutgoingTelegramReply));

        var actor = CreateActor(StatementExtractionResult.Success([]));
        actor.Tell(Document());

        outbox.ExpectMsg<OutgoingTelegramReply>(r => r.Text.Should().Contain("CSV"));
    }

    [Fact]
    public void Extraction_failure_replies_error()
    {
        var outbox = CreateTestProbe();
        Sys.EventStream.Subscribe(outbox.Ref, typeof(OutgoingTelegramReply));

        var actor = CreateActor(StatementExtractionResult.Failure(ClaudeUnavailabilityReason.RateLimited, "429"));
        actor.Tell(Photo());

        outbox.ExpectMsg<OutgoingTelegramReply>(r => r.Text.Should().Contain("Распознаю"));
        outbox.ExpectMsg<OutgoingTelegramReply>(r => r.Text.Should().Contain("распознать"));
    }

    [Fact]
    public void Empty_extraction_replies_no_transactions()
    {
        var outbox = CreateTestProbe();
        Sys.EventStream.Subscribe(outbox.Ref, typeof(OutgoingTelegramReply));

        var actor = CreateActor(StatementExtractionResult.Success([]));
        actor.Tell(Photo());

        outbox.ExpectMsg<OutgoingTelegramReply>(r => r.Text.Should().Contain("Распознаю"));
        outbox.ExpectMsg<OutgoingTelegramReply>(r => r.Text.Should().Contain("Не нашёл"));
    }

    private sealed class StubTelegramBot(TelegramFileDownload download) : ITelegramBot
    {
        public Task<TelegramFileDownload> DownloadFileAsync(string fileId, CancellationToken ct) => Task.FromResult(download);
        public Task SendTextAsync(long chatId, string text, CancellationToken ct) => Task.CompletedTask;
        public Task SendInlineKeyboardAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> rows, CancellationToken ct) => Task.CompletedTask;
        public Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken ct) => Task.CompletedTask;
        public Task SendPhotoAsync(long chatId, byte[] photo, string fileName, string? caption, CancellationToken ct) => Task.CompletedTask;
        public Task SendDocumentAsync(long chatId, byte[] document, string fileName, string? caption, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> SetWebhookAsync(string url, CancellationToken ct) => Task.FromResult(true);
        public Task DeleteWebhookAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<TelegramPollResult> PollAsync(long offset, TimeSpan timeout, CancellationToken ct) => Task.FromResult(new TelegramPollResult([], [], offset));
    }

    private sealed class StubExtractor(StatementExtractionResult result) : IStatementExtractor
    {
        public Task<StatementExtractionResult> ExtractAsync(ReadOnlyMemory<byte> image, string mediaType, CancellationToken ct) => Task.FromResult(result);
    }
}
