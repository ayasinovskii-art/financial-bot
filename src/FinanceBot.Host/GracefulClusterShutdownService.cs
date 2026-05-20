using Akka.Actor;
using Akka.Cluster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Host;

/// <summary>
/// При SIGTERM просит Akka cluster выполнить graceful leave через CoordinatedShutdown.
/// Ждём окончания leave с таймаутом, чтобы in-flight операции успели завершиться.
/// </summary>
public sealed class GracefulClusterShutdownService(
    ActorSystem actorSystem,
    ILogger<GracefulClusterShutdownService> log)
    : IHostedService
{
    private static readonly TimeSpan LeaveTimeout = TimeSpan.FromSeconds(30);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cluster = Cluster.Get(actorSystem);
            log.LogInformation("Initiating cluster leave for {Address}…", cluster.SelfAddress);
            var shutdown = CoordinatedShutdown.Get(actorSystem);
            await shutdown.Run(CoordinatedShutdown.ClusterLeavingReason.Instance).WaitAsync(LeaveTimeout, cancellationToken);
            log.LogInformation("Cluster leave completed.");
        }
        catch (TimeoutException)
        {
            log.LogWarning("Cluster leave timed out after {Timeout}.", LeaveTimeout);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Cluster leave failed.");
        }
    }
}
