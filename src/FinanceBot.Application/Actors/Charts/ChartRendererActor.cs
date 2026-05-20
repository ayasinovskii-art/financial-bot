using Akka.Actor;
using Akka.Event;
using Akka.Routing;
using FinanceBot.Application.Actors.Common;
using FinanceBot.Domain.Events.Reports;

namespace FinanceBot.Application.Actors.Charts;

/// <summary>
/// Worker: рендерит один график. Создаётся router'ом (RoundRobinPool) на каждом ноде в количестве N штук.
/// </summary>
public sealed class ChartRendererActor : ReceiveActor
{
    private readonly IChartRenderer _renderer;
    private readonly ILoggingAdapter _log;

    public ChartRendererActor(IChartRenderer renderer)
    {
        _renderer = renderer;
        _log = Context.GetLogger();

        Receive<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));
        Receive<RenderChartRequest>(HandleRender);
    }

    private void HandleRender(RenderChartRequest req)
    {
        try
        {
            var bytes = _renderer.Render(req.Data, req.Width, req.Height);
            Sender.Tell(new RenderChartResponse(req.CorrelationId, req.UserId, req.Type, bytes, null));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "ChartRendererActor: render failed for {Type}.", req.Type);
            Sender.Tell(new RenderChartResponse(req.CorrelationId, req.UserId, req.Type, Array.Empty<byte>(), ex.Message));
        }
    }

    public static Props CreateProps(IChartRenderer renderer) =>
        Props.Create(() => new ChartRendererActor(renderer));

    public static Props CreatePoolProps(IChartRenderer renderer, int workers) =>
        CreateProps(renderer).WithRouter(new RoundRobinPool(workers));
}

/// <summary>Marker для регистрации пула в registry.</summary>
public sealed class ChartRendererPoolMarker;

public sealed record RenderChartRequest(
    Guid CorrelationId,
    Guid UserId,
    ChartType Type,
    ChartDataSet Data,
    int Width = 1024,
    int Height = 640);

public sealed record RenderChartResponse(
    Guid CorrelationId,
    Guid UserId,
    ChartType Type,
    byte[] PngBytes,
    string? ErrorMessage);
