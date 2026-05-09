using System.ComponentModel.DataAnnotations;

namespace FinanceBot.Application.Configuration;

/// <summary>
/// Опции Akka из appsettings.Akka. Используется ApplicationServiceCollectionExtensions.
/// </summary>
public sealed class AkkaOptions
{
    public const string SectionName = "Akka";

    [Required]
    public string ClusterName { get; init; } = "financebot";

    [Required]
    public string Hostname { get; init; } = "127.0.0.1";

    [Range(1, 65535)]
    public int Port { get; init; } = 4053;

    public DiscoveryOptions Discovery { get; init; } = new();

    public ClusterOptions Cluster { get; init; } = new();

    public sealed class DiscoveryOptions
    {
        [Required]
        public string Method { get; init; } = "config";
    }

    public sealed class ClusterOptions
    {
        public string[] SeedNodes { get; init; } = [];

        [Range(1, 100)]
        public int MinimumMembers { get; init; } = 1;

        [Range(1, 10000)]
        public int ShardCount { get; init; } = 100;

        public string ShardCoordinatorMode { get; init; } = "ddata";
    }
}
