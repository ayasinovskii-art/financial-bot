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
/// Рендер графиков. На <see cref="RequestChart"/> читает данные из read-model
/// через <see cref="IChartDataReader"/>, шлёт в <see cref="ChartRendererActor"/> pool (RoundRobin)
/// и публикует PNG в Telegram через EventStream(OutgoingTelegramPhoto).
/// </summary>
public sealed partial class UserActor
{
    private readonly Dictionary<Guid, PendingChart> _pendingCharts = new();

    partial void WireCharts()
    {
        Recover<ChartRequested>(_ => { /* informational */ });
        Recover<ChartGenerated>(_ => { /* informational */ });

        Command<RequestChart>(OnRequestChart);
        Command<ChartDataLoaded>(OnChartDataLoaded);
        Command<RenderChartResponse>(OnRenderChartResponse);
    }

    private void OnRequestChart(RequestChart cmd)
    {
        if (!_state.IsRegistered || _state.TelegramId is not { } chatId)
        {
            return;
        }
        if (!TryParseChartType(cmd.ChartType, out var type))
        {
            Context.System.EventStream.Publish(new OutgoingTelegramReply(chatId,
                "Неизвестный тип графика. Доступно: category, daily, buckets, savings."));
            return;
        }

        var corr = Guid.NewGuid();
        _pendingCharts[corr] = new PendingChart(type, chatId, ServiceProviderHost.Resolve<IChartDataReader>(Context.System));

        var evt = new ChartRequested(_userId, type, cmd.Params, DateTimeOffset.UtcNow);
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            StartChartLoad(corr, persisted.ChartType);
        });
    }

    private void StartChartLoad(Guid corr, ChartType type)
    {
        if (!_pendingCharts.TryGetValue(corr, out var ctx))
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
        if (!_pendingCharts.TryGetValue(msg.CorrelationId, out var ctx))
        {
            return;
        }
        if (msg.Data is null)
        {
            _pendingCharts.Remove(msg.CorrelationId);
            Context.System.EventStream.Publish(new OutgoingTelegramReply(ctx.ChatId,
                msg.ErrorMessage is null ? "Нет данных для графика." : $"Не удалось собрать данные: {msg.ErrorMessage}"));
            return;
        }

        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<ChartRendererPoolMarker>(out var pool))
        {
            _pendingCharts.Remove(msg.CorrelationId);
            Context.System.EventStream.Publish(new OutgoingTelegramReply(ctx.ChatId, "Сервис рендера графиков не запущен."));
            return;
        }
        pool.Tell(new RenderChartRequest(msg.CorrelationId, _userId, msg.Type, msg.Data));
    }

    private void OnRenderChartResponse(RenderChartResponse resp)
    {
        if (!_pendingCharts.Remove(resp.CorrelationId, out var ctx))
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
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
        });
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

    /// <summary>
    /// Hook вечернего опроса: при выходе из вечернего FSM автоматически отрисовать category-pie
    /// и опубликовать его пользователю. Идёт через тот же pipeline, что и /chart category.
    /// Если pool или reader недоступны — silent skip (актуально для unit-тестов).
    /// </summary>
    internal void TriggerEveningCategoryChart()
    {
        if (_state.TelegramId is not { } chatId)
        {
            return;
        }
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
        _pendingCharts[corr] = new PendingChart(ChartType.CategoryPie, chatId, reader);
        var evt = new ChartRequested(_userId, ChartType.CategoryPie, "evening-summary", DateTimeOffset.UtcNow);
        Persist(evt, _ =>
        {
            MaybeSnapshot();
            StartChartLoad(corr, ChartType.CategoryPie);
        });
    }

    private sealed record PendingChart(ChartType Type, long ChatId, IChartDataReader Reader);

    private sealed record ChartDataLoaded(Guid CorrelationId, ChartType Type, ChartDataSet? Data, string? ErrorMessage);
}

/// <summary>
/// Лёгкий хост для резолва singleton сервисов из ActorSystem extensions.
/// </summary>
internal static class ServiceProviderHost
{
    public static T Resolve<T>(Akka.Actor.ActorSystem system) where T : class
    {
        var svc = Akka.DependencyInjection.DependencyResolver.For(system)
            .Resolver.GetService(typeof(T)) as T;
        return svc ?? throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
    }
}
