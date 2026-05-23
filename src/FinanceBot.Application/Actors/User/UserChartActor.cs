using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Persistence;
using FinanceBot.Application.Actors.Charts;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.Events.Reports;

namespace FinanceBot.Application.Actors.User;

/// <summary>
/// Per-user child actor: рендер графиков.
/// <see cref="PersistenceId"/> = <c>user-{userId:N}-chart</c>.
/// Recovers <see cref="ChartRequested"/>/<see cref="ChartGenerated"/> только как информационные
/// (state не строится). Handlers идентичны логике из старого <c>UserActor.Charts.cs</c>.
/// </summary>
public sealed class UserChartActor : ReceivePersistentActor
{
    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;
    private readonly Dictionary<Guid, PendingChart> _pending = new();

    public override string PersistenceId { get; }

    public UserChartActor(Guid userId)
    {
        _userId = userId;
        _log = Context.GetLogger();
        PersistenceId = $"user-{userId:N}-chart";

        Recover<ChartRequested>(_ => { });
        Recover<ChartGenerated>(_ => { });

        Command<EnrichedChartRequest>(OnRequest);
        Command<EveningChartTrigger>(OnEveningTrigger);
        Command<ChartDataLoaded>(OnChartDataLoaded);
        Command<RenderChartResponse>(OnRenderChartResponse);
    }

    private void OnRequest(EnrichedChartRequest msg)
    {
        if (!TryParseChartType(msg.Request.ChartType, out var type))
        {
            Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.TelegramId,
                "Неизвестный тип графика. Доступно: category, daily, buckets, savings."));
            return;
        }

        var corr = Guid.NewGuid();
        _pending[corr] = new PendingChart(type, msg.TelegramId, ServiceProviderHost.Resolve<IChartDataReader>(Context.System));

        var evt = new ChartRequested(_userId, type, msg.Request.Params, DateTimeOffset.UtcNow);
        Persist(evt, persisted => StartChartLoad(corr, persisted.ChartType));
    }

    /// <summary>
    /// Триггер от <see cref="UserActor"/> EveningSurvey: после выхода из FSM
    /// автоматически отрисовать category-pie. Silent skip если pool/reader недоступны.
    /// </summary>
    private void OnEveningTrigger(EveningChartTrigger trigger)
    {
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<ChartRendererPoolMarker>(out _))
        {
            return;
        }
        IChartDataReader reader;
        try
        {
            reader = ServiceProviderHost.Resolve<IChartDataReader>(Context.System);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var corr = Guid.NewGuid();
        _pending[corr] = new PendingChart(ChartType.CategoryPie, trigger.TelegramId, reader);
        var evt = new ChartRequested(_userId, ChartType.CategoryPie, "evening-summary", DateTimeOffset.UtcNow);
        Persist(evt, _ => StartChartLoad(corr, ChartType.CategoryPie));
    }

    private void StartChartLoad(Guid corr, ChartType type)
    {
        if (!_pending.TryGetValue(corr, out var ctx))
        {
            return;
        }
        var self = Self;
        var reader = ctx.Reader;
        var userId = _userId;
        Task.Run(async () =>
        {
            try
            {
                var data = await reader.LoadAsync(userId, type, CancellationToken.None).ConfigureAwait(false);
                self.Tell(new ChartDataLoaded(corr, type, data, ErrorMessage: null));
            }
            catch (Exception ex)
            {
                self.Tell(new ChartDataLoaded(corr, type, null, ex.Message));
            }
        });
    }

    private void OnChartDataLoaded(ChartDataLoaded msg)
    {
        if (!_pending.TryGetValue(msg.CorrelationId, out var ctx))
        {
            return;
        }
        if (msg.Data is null)
        {
            _pending.Remove(msg.CorrelationId);
            Context.System.EventStream.Publish(new OutgoingTelegramReply(ctx.ChatId,
                msg.ErrorMessage is null ? "Нет данных для графика." : $"Не удалось собрать данные: {msg.ErrorMessage}"));
            return;
        }

        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<ChartRendererPoolMarker>(out var pool))
        {
            _pending.Remove(msg.CorrelationId);
            Context.System.EventStream.Publish(new OutgoingTelegramReply(ctx.ChatId, "Сервис рендера графиков не запущен."));
            return;
        }
        pool.Tell(new RenderChartRequest(msg.CorrelationId, _userId, msg.Type, msg.Data));
    }

    private void OnRenderChartResponse(RenderChartResponse resp)
    {
        if (!_pending.Remove(resp.CorrelationId, out var ctx))
        {
            return;
        }
        if (resp.PngBytes.Length == 0)
        {
            Context.System.EventStream.Publish(new OutgoingTelegramReply(ctx.ChatId,
                resp.ErrorMessage is null ? "Не удалось отрисовать график." : $"Ошибка рендера: {resp.ErrorMessage}"));
            return;
        }

        var fileName = $"chart_{resp.Type.ToString().ToLowerInvariant()}.png";
        Context.System.EventStream.Publish(new OutgoingTelegramPhoto(ctx.ChatId, resp.PngBytes, fileName, Caption: resp.Type.ToString()));

        var evt = new ChartGenerated(_userId, resp.Type, resp.PngBytes.LongLength, DateTimeOffset.UtcNow);
        Persist(evt, _ => { });
    }

    private static bool TryParseChartType(string? raw, out ChartType type)
    {
        type = ChartType.CategoryPie;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }
        switch (raw.Trim().ToLowerInvariant())
        {
            case "category":
            case "categories":
            case "pie":
                type = ChartType.CategoryPie; return true;
            case "daily":
            case "days":
                type = ChartType.DailyBar; return true;
            case "buckets":
            case "bucket":
                type = ChartType.BucketUtilization; return true;
            case "savings":
            case "deposit":
                type = ChartType.SavingsLine; return true;
            default: return false;
        }
    }

    public static Props CreateProps(Guid userId) => Props.Create(() => new UserChartActor(userId));

    private sealed record PendingChart(ChartType Type, long ChatId, IChartDataReader Reader);

    private sealed record ChartDataLoaded(Guid CorrelationId, ChartType Type, ChartDataSet? Data, string? ErrorMessage);
}

/// <summary>Обёртка над <see cref="RequestChart"/>: parent добавляет TelegramId.</summary>
public sealed record EnrichedChartRequest(RequestChart Request, long TelegramId);

/// <summary>Триггер от EveningSurvey на авто-отрисовку category-pie.</summary>
public sealed record EveningChartTrigger(long TelegramId);
