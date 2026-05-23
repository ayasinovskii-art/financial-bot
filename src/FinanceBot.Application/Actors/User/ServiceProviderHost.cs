using Akka.Actor;
using Akka.DependencyInjection;

namespace FinanceBot.Application.Actors.User;

/// <summary>
/// Лёгкий хост для резолва singleton-сервисов из <see cref="ActorSystem"/> extensions.
/// </summary>
internal static class ServiceProviderHost
{
    public static T Resolve<T>(ActorSystem system) where T : class
    {
        var svc = DependencyResolver.For(system)
            .Resolver.GetService(typeof(T)) as T;
        return svc ?? throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
    }
}
