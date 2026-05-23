using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.Reports;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.User;

/// <summary>
/// Команда /report [period]. periodsAgo: 0=current (default), 1=previous, N — назад.
/// UserActor вычитывает текстовый отчёт через IReportBuilder и публикует через EventStream.
/// </summary>
public sealed partial class UserActor
{
    partial void WireReports()
    {
        Command<RequestReport>(OnRequestReport);
        Command<ReportReady>(OnReportReady);
    }

    private void OnRequestReport(RequestReport cmd)
    {
        if (!_state.IsRegistered || _state.TelegramId is not { } chatId)
        {
            return;
        }
        var periodsAgo = ParsePeriod(cmd.Period);
        IReportBuilder builder;
        try
        {
            builder = ServiceProviderHost.Resolve<IReportBuilder>(Context.System);
        }
        catch (InvalidOperationException ex)
        {
            _log.Warning(ex, "ReportBuilder not registered.");
            Context.System.EventStream.Publish(new OutgoingTelegramReply(chatId, "Сервис отчётов не доступен."));
            return;
        }

        var self = Self;
        var userId = _userId;
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

    private void OnReportReady(ReportReady msg)
    {
        if (msg.ErrorMessage is not null)
        {
            _log.Warning("Report build failed: {Error}", msg.ErrorMessage);
            Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.ChatId, $"Не удалось собрать отчёт: {msg.ErrorMessage}"));
            return;
        }
        var result = msg.ReportResult!;
        Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.ChatId, result.Text));
    }

    /// <summary>Парсинг "current"/"previous"/"N" в periodsAgo (0/1/N).</summary>
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

    private sealed record ReportReady(long ChatId, ReportResult? ReportResult, string? ErrorMessage);
}
