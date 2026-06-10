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

        Receive<EnrichedReportRequest>(OnRequest);
        Receive<ReportReady>(OnReportReady);
        Receive<EnrichedExportRequest>(OnExportRequest);
        Receive<ExportReady>(OnExportReady);
    }

    private void OnRequest(EnrichedReportRequest msg)
    {
        var periodsAgo = ParsePeriod(msg.Request.Period);
        IReportBuilder builder;
        try
        {
            builder = ServiceProviderHost.Resolve<IReportBuilder>(Context.System);
        }
        catch (InvalidOperationException ex)
        {
            _log.Warning(ex, "ReportBuilder not registered.");
            Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.TelegramId, "Сервис отчётов не доступен."));
            return;
        }

        var self = Self;
        var userId = _userId;
        var chatId = msg.TelegramId;
        Task.Run(async () =>
        {
            try
            {
                var result = await builder.BuildAsync(userId, periodsAgo, CancellationToken.None).ConfigureAwait(false);
                self.Tell(new ReportReady(chatId, result, ErrorMessage: null));
            }
            catch (Exception ex)
            {
                self.Tell(new ReportReady(chatId, ReportResult: null, ErrorMessage: ex.Message));
            }
        });
    }

    private void OnExportRequest(EnrichedExportRequest msg)
    {
        var periodsAgo = ParsePeriod(msg.Request.Period);
        IReportBuilder builder;
        try
        {
            builder = ServiceProviderHost.Resolve<IReportBuilder>(Context.System);
        }
        catch (InvalidOperationException ex)
        {
            _log.Warning(ex, "ReportBuilder not registered.");
            Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.TelegramId, "Сервис отчётов не доступен."));
            return;
        }

        var self = Self;
        var userId = _userId;
        var chatId = msg.TelegramId;
        Task.Run(async () =>
        {
            try
            {
                var result = await builder.ExportExpensesAsync(userId, periodsAgo, CancellationToken.None).ConfigureAwait(false);
                self.Tell(new ExportReady(chatId, result, ErrorMessage: null));
            }
            catch (Exception ex)
            {
                self.Tell(new ExportReady(chatId, ExportResult: null, ErrorMessage: ex.Message));
            }
        });
    }

    private void OnExportReady(ExportReady msg)
    {
        if (msg.ErrorMessage is not null)
        {
            _log.Warning("Export build failed: {0}", msg.ErrorMessage);
            Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.ChatId, $"Не удалось собрать выгрузку: {msg.ErrorMessage}"));
            return;
        }
        if (!msg.ExportResult!.HasData)
        {
            Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.ChatId, "Нечего выгружать: трат за период нет."));
            return;
        }
        Context.System.EventStream.Publish(new OutgoingTelegramDocument(
            msg.ChatId, msg.ExportResult.Content, msg.ExportResult.FileName, Caption: "Траты периода (CSV)"));
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
    private sealed record ExportReady(long ChatId, ExportResult? ExportResult, string? ErrorMessage);
}

public sealed record EnrichedReportRequest(RequestReport Request, long TelegramId);

public sealed record EnrichedExportRequest(RequestExport Request, long TelegramId);
