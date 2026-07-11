using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Eventing;

/// <summary>
/// Adds eventing services and their explicit observer policies.
/// </summary>
public static class EventingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the only selected observer policy for one named fan-out boundary.
    /// </summary>
    public static IServiceCollection AddContinueAndReportObserverPolicy(
        this IServiceCollection services,
        ObserverFailurePolicyKey boundary
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary.Value);

        services.AddKeyedSingleton(
            boundary,
            new ContinueAndReportObserverPolicy { Boundary = boundary }
        );
        return services;
    }

    /// <summary>
    /// Registers an event bus with its required observer policy.
    /// </summary>
    public static IServiceCollection AddEventBus<TKey>(
        this IServiceCollection services,
        ObserverFailurePolicyKey observerPolicy
    )
        where TKey : notnull
    {
        services.AddContinueAndReportObserverPolicy(observerPolicy);
        services.AddSingleton<EventBus<TKey>>();
        return services;
    }
}
