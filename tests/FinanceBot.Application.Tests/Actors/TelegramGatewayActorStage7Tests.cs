using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.Telegram.Commands;
using FinanceBot.Application.Actors.Telegram.Commands.Handlers;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Csv;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Commands.AccessControl;
using FinanceBot.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class TelegramGatewayActorStage7Tests : TestKit
{
    private const long TelegramId = 3001L;
    private const long ChatId = 4002L;
    private const string CsvFileId = "csvfile456";

    private static IncomingTelegramUpdate MakeDocumentUpdate(
        string? mimeType = "text/csv",
        string? fileName = "expenses.csv",
        string? fileId = CsvFileId) =>
        new(UpdateId: 1, ChatId: ChatId, TelegramId: TelegramId,
            Username: null, FirstName: null, LastName: null,
            Text: null, SentAt: DateTimeOffset.UtcNow,
            DocumentFileId: fileId, DocumentFileName: fileName, DocumentMimeType: mimeType);

    private static IncomingTelegramUpdate MakeTextUpdate(string text) =>
        new(UpdateId: 2, ChatId: ChatId, TelegramId: TelegramId,
            Username: null, FirstName: null, LastName: null,
            Text: text, SentAt: DateTimeOffset.UtcNow);

    private (IActorRef gateway, TestProbe accessProbe) SetupGateway(
        ICsvImportParser? parser = null,
        IEnumerable<ITelegramCommandHandler>? commandHandlers = null)
    {
        var accessProbe = CreateTestProbe();
        var registry = ActorRegistry.For(Sys);
        registry.Register<AccessControlSingletonMarker>(accessProbe.Ref);

        var gateway = Sys.ActorOf(TelegramGatewayActor.CreateProps(
            Options.Create(new UserDefaultsOptions()),
            commandHandlers ?? [],
            [],
            new NlpPendingCache(),
            new ImportPendingCache(),
            new StubTelegramBot(),
            parser ?? new EmptyStubParser()));

        return (gateway, accessProbe);
    }

    private void AllowAccess(TestProbe accessProbe)
    {
        accessProbe.ExpectMsg<IsAllowed>(TimeSpan.FromSeconds(3));
        accessProbe.Reply(new AccessDecision.Allowed(TelegramId, AccessRole.User));
    }

    [Fact]
    public void Csv_document_update_publishes_InlineKeyboard_with_two_buttons()
    {
        var kbProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(kbProbe, typeof(OutgoingInlineKeyboard));

        var row = new ParsedImportRow(500m, new DateOnly(2026, 1, 1), "Кофе");
        var parser = new SingleRowStubParser(row);
        var (gateway, accessProbe) = SetupGateway(parser: parser);

        gateway.Tell(MakeDocumentUpdate());
        AllowAccess(accessProbe);

        var kb = kbProbe.ExpectMsg<OutgoingInlineKeyboard>(TimeSpan.FromSeconds(5));
        Assert.Equal(ChatId, kb.ChatId);
        Assert.Single(kb.Rows);
        Assert.Equal(2, kb.Rows[0].Count);
    }

    [Fact]
    public void Non_csv_document_does_not_publish_InlineKeyboard()
    {
        var kbProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(kbProbe, typeof(OutgoingInlineKeyboard));

        var (gateway, accessProbe) = SetupGateway();

        gateway.Tell(MakeDocumentUpdate(mimeType: "application/pdf", fileName: "report.pdf"));
        AllowAccess(accessProbe);

        kbProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public void Empty_parse_result_publishes_CsvImportEmptyFile_reply()
    {
        var replyProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(replyProbe, typeof(OutgoingTelegramReply));

        var (gateway, accessProbe) = SetupGateway(parser: new EmptyStubParser());

        gateway.Tell(MakeDocumentUpdate());
        AllowAccess(accessProbe);

        var reply = replyProbe.ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(5));
        Assert.Equal(ChatId, reply.ChatId);
        Assert.Equal(TelegramReplies.CsvImportEmptyFile(), reply.Text);
    }

    [Fact]
    public void Import_command_publishes_instruction_reply()
    {
        var replyProbe = CreateTestProbe();
        Sys.EventStream.Subscribe(replyProbe, typeof(OutgoingTelegramReply));

        var (gateway, accessProbe) = SetupGateway(commandHandlers: [new ImportCommandHandler()]);

        gateway.Tell(MakeTextUpdate("/import"));
        AllowAccess(accessProbe);

        var reply = replyProbe.ExpectMsg<OutgoingTelegramReply>(TimeSpan.FromSeconds(3));
        Assert.Equal(TelegramReplies.CsvImportInstruction(), reply.Text);
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

    private sealed class EmptyStubParser : ICsvImportParser
    {
        public CsvParseResult Parse(string csvText) => new([], 0);
    }

    private sealed class SingleRowStubParser : ICsvImportParser
    {
        private readonly ParsedImportRow _row;
        public SingleRowStubParser(ParsedImportRow row) => _row = row;
        public CsvParseResult Parse(string csvText) => new([_row], 0);
    }
}
