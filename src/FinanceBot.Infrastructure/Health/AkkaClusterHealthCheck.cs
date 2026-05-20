using Akka.Actor;
using Akka.Cluster;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinanceBot.Infrastructure.Health;

/// <summary>
/// Health check на состояние Akka cluster: member count и наличие unreachable нод.
/// Возвращает Unhealthy если кластер ещё не сформирован, или есть unreachable нода.
/// </summary>
public sealed class AkkaClusterHealthCheck(ActorSystem actorSystem) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cluster = Cluster.Get(actorSystem);
            var state = cluster.State;

            var members = state.Members.Count;
            var unreachable = state.Unreachable.Count;

            var data = new Dictionary<string, object>
            {
                ["members"] = members,
                ["unreachable"] = unreachable,
                ["self"] = cluster.SelfAddress.ToString(),
                ["selfStatus"] = state.Members.FirstOrDefault(m => m.Address == cluster.SelfAddress)?.Status.ToString() ?? "Unknown"
            };

            if (members == 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("No cluster members.", data: data));
            }
            if (unreachable > 0)
            {
                return Task.FromResult(HealthCheckResult.Degraded($"{unreachable} unreachable nodes.", data: data));
            }
            return Task.FromResult(HealthCheckResult.Healthy("Akka cluster healthy.", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Akka cluster health check threw.", ex));
        }
    }
}
