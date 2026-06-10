using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.Reports;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.User;

/// <summary>
/// Per-user child actor: сборка текстового отчёта через <see cref="IReportBuilder"/>.
/// Stateless — нет persistence. Parent форвардит <see cref="EnrichedReportRequest"/> с TelegramId.
/// </summary>
public sealed class UserReportActor : ReceiveActor
{
    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;

    public UserReportActor(Guid userId)
    {
        _userId = userId;
        _log = Context.GetLogger();

        Receive<EnrichedReportRequest>(msg => OnRequest(msg.TelegramId, msg.Request.Period, Mode.Report));
        Receive<EnrichedStatsRequest>(msg => OnRequest(msg.TelegramId, msg.Request.Period, Mode.Stats));
        Receive<ReportReady>(OnReportReady);
    }

    private enum Mode { Report, Stats }

    private void OnRequest(long telegramId, string? rawPeriod, Mode mode)
    {
        var periodsAgo = ParsePeriod(rawPeriod);
        IReportBuilder builder;
        try
        {
            builder = ServiceProviderHost.Resolve<IReportBuilder>(Context.System);
        }
        catch (InvalidOperationException ex)
        {
            _log.Warning(ex, "ReportBuilder not registered.");
            Context.System.EventStream.Publish(new OutgoingTelegramReply(telegramId, "Сервис отчётов не доступен."));
            return;
        }

        var self = Self;
        var userId = _userId;
        var chatId = telegramId;
        Task.Run(async () =>
        {
            try
            {
                var result = mode == Mode.Stats
                    ? await builder.BuildStatsAsync(userId, periodsAgo, CancellationToken.None).ConfigureAwait(false)
                    : await builder.BuildAsync(userId, periodsAgo, CancellationToken.None).ConfigureAwait(false);
                self.Tell(new ReportReady(chatId, result, ErrorMessage: null));
            }
            catch (Exception ex)
            {
                self.Tell(new ReportReady(chatId, ReportResult: null, ErrorMessage: ex.Message));
            }
        });
    }

    private void OnReportReady(ReportReady msg)
    {
        if (msg.ErrorMessage is not null)
        {
            _log.Warning("Report build failed: {0}", msg.ErrorMessage);
            Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.ChatId, $"Не удалось собрать отчёт: {msg.ErrorMessage}"));
            return;
        }
        Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.ChatId, msg.ReportResult!.Text));
    }

    private static int ParsePeriod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var v = raw.Trim().ToLowerInvariant();
        return v switch
        {
            "current" or "" => 0,
            "previous" or "prev" or "last" => 1,
            _ => int.TryParse(v, out var n) && n >= 0 ? n : 0
        };
    }

    public static Props CreateProps(Guid userId) => Props.Create(() => new UserReportActor(userId));

    private sealed record ReportReady(long ChatId, ReportResult? ReportResult, string? ErrorMessage);
}

public sealed record EnrichedReportRequest(RequestReport Request, long TelegramId);

public sealed record EnrichedStatsRequest(RequestStats Request, long TelegramId);
