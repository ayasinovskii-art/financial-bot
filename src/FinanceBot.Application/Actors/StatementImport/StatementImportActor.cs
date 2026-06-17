using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.Services;

namespace FinanceBot.Application.Actors.StatementImport;

/// <summary>Marker для регистрации StatementImportActor в ActorRegistry.</summary>
public sealed class StatementImportActorMarker;

/// <summary>
/// Per-node сервис импорта выписки: скачивает фото, распознаёт через <see cref="IStatementExtractor"/>
/// (Claude Vision), просит UserActor сохранить pending-предложение и отправляет inline-подтверждение.
/// Тонкий оркестратор — доменная логика импорта живёт в UserActor.
/// </summary>
public sealed class StatementImportActor : ReceiveActor
{
    /// <summary>Префикс callback_data кнопок подтверждения импорта.</summary>
    public const string CallbackPrefix = "import:";

    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(30);

    private readonly ITelegramBot _bot;
    private readonly IStatementExtractor _extractor;
    private readonly ILoggingAdapter _log;

    public StatementImportActor(ITelegramBot bot, IStatementExtractor extractor)
    {
        _bot = bot;
        _extractor = extractor;
        _log = Context.GetLogger();

        Receive<IncomingTelegramFile>(OnFile);
        Receive<ExtractionOutcome>(OnExtractionOutcome);
        Receive<ProposalOutcome>(OnProposalOutcome);
    }

    public static Props CreateProps(ITelegramBot bot, IStatementExtractor extractor)
        => Props.Create(() => new StatementImportActor(bot, extractor));

    private void OnFile(IncomingTelegramFile file)
    {
        if (file.Kind != FileKind.Photo)
        {
            Publish(file.ChatId, "Пока умею импортировать только скриншот выписки (фото). Поддержка CSV — скоро.");
            return;
        }

        var bot = _bot;
        var extractor = _extractor;
        Task.Run<ExtractionOutcome>(async () =>
        {
            try
            {
                var download = await bot.DownloadFileAsync(file.FileId, CancellationToken.None).ConfigureAwait(false);
                if (download.Bytes.Length == 0)
                {
                    return new ExtractionOutcome(file, null, "empty download");
                }
                var result = await extractor.ExtractAsync(download.Bytes, download.MediaType, CancellationToken.None).ConfigureAwait(false);
                return new ExtractionOutcome(file, result, null);
            }
            catch (Exception ex)
            {
                return new ExtractionOutcome(file, null, ex.Message);
            }
        }).PipeTo(Self);

        Publish(file.ChatId, "Распознаю выписку, секунду…");
    }

    private void OnExtractionOutcome(ExtractionOutcome outcome)
    {
        var file = outcome.File;
        if (outcome.Result is not { } result)
        {
            _log.Warning("Statement extract failed for chat {ChatId}: {Error}", file.ChatId, outcome.Error);
            Publish(file.ChatId, "Не смог обработать изображение. Попробуй ещё раз позже.");
            return;
        }
        if (!result.IsSuccess)
        {
            Publish(file.ChatId, "Не смог распознать выписку, попробуй позже.");
            return;
        }
        if (result.Transactions.Count == 0)
        {
            Publish(file.ChatId, "Не нашёл транзакций на изображении. Пришли скриншот покрупнее.");
            return;
        }

        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            Publish(file.ChatId, "Импорт временно недоступен.");
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(file.TelegramId);
        var proposalId = Guid.NewGuid();
        var chatId = file.ChatId;
        userShard.Ask<object>(
            new ShardEnvelope(userId.ToString("N"), new ProposeStatementImport(userId, proposalId, result.Transactions)),
            AskTimeout)
            .ContinueWith(t => new ProposalOutcome(chatId, t.IsCompletedSuccessfully ? t.Result : null))
            .PipeTo(Self);
    }

    private void OnProposalOutcome(ProposalOutcome outcome)
    {
        switch (outcome.Reply)
        {
            case StatementImportProposed p:
                var text =
                    $"Распознал {p.Count} транзакц(ий): расходы {p.ExpenseTotal:0.00} ₽ ({p.ExpenseCount}), " +
                    $"доходы {p.IncomeTotal:0.00} ₽ ({p.IncomeCount}). Импортировать?";
                var rows = new IReadOnlyList<InlineButton>[]
                {
                    new[] { new InlineButton($"Импортировать {p.Count}", $"{CallbackPrefix}confirm:{p.ProposalId:N}") },
                    new[]
                    {
                        new InlineButton("Список", $"{CallbackPrefix}list:{p.ProposalId:N}"),
                        new InlineButton("Отмена", $"{CallbackPrefix}cancel:{p.ProposalId:N}")
                    }
                };
                Context.System.EventStream.Publish(new OutgoingInlineKeyboard(outcome.ChatId, text, rows));
                return;
            case StatementImportRejected r:
                Publish(outcome.ChatId, r.Reason);
                return;
            default:
                Publish(outcome.ChatId, "Не удалось подготовить импорт. Попробуй позже.");
                return;
        }
    }

    private void Publish(long chatId, string text)
        => Context.System.EventStream.Publish(new OutgoingTelegramReply(chatId, text));

    private sealed record ExtractionOutcome(IncomingTelegramFile File, StatementExtractionResult? Result, string? Error);

    private sealed record ProposalOutcome(long ChatId, object? Reply);
}
