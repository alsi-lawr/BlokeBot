using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Eventing;

public static class EventingServiceCollectionExtensions
{
    public static IServiceCollection AddObserverFanOut<TBoundary, TEvent, TDeadLetter>(
        this IServiceCollection services,
        ObserverFailurePolicy<TBoundary, TDeadLetter> policy
    )
        where TDeadLetter : IObserverDeadLetterPayload
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.Boundary.Value);

        services.TryAddSingleton<
            IObserverFailureDiagnosticReporter,
            ObserverFailureDiagnosticLogger
        >();
        services.TryAddSingleton<IObserverCorrelationIdProvider, ObserverCorrelationIdProvider>();
        _ = services.AddSingleton(policy);
        _ = services.AddSingleton<ObserverFanOut<TBoundary, TEvent, TDeadLetter>>(
            static serviceProvider => new ObserverFanOut<TBoundary, TEvent, TDeadLetter>(
                serviceProvider.GetRequiredService<ObserverFailurePolicy<TBoundary, TDeadLetter>>(),
                serviceProvider.GetRequiredService<IObserverFailureDiagnosticReporter>(),
                serviceProvider.GetRequiredService<IObserverCorrelationIdProvider>()
            )
        );
        return services;
    }

    public static IServiceCollection AddContinueAndReportObserverFanOut<
        TBoundary,
        TEvent,
        TDeadLetter
    >(this IServiceCollection services, ObserverBoundary boundary)
        where TDeadLetter : IObserverDeadLetterPayload =>
        services.AddObserverFanOut<TBoundary, TEvent, TDeadLetter>(
            new ObserverFailurePolicy<TBoundary, TDeadLetter>.ContinueAndReport
            {
                Boundary = boundary,
            }
        );

    public static IServiceCollection AddEventBus<TKey>(
        this IServiceCollection services,
        ObserverBoundary boundary,
        Func<TKey, ObserverEventIdentity> eventIdentity
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(eventIdentity);

        _ = services.AddContinueAndReportObserverFanOut<
            EventBusObserverBoundary<TKey>,
            EventNotification<TKey>,
            EventBusDeadLetter
        >(boundary);
        _ = services.AddSingleton(new EventBusEventIdentity<TKey> { Project = eventIdentity });
        _ = services.AddSingleton<EventBus<TKey>>(static serviceProvider => new EventBus<TKey>(
            serviceProvider.GetRequiredService<
                ObserverFanOut<
                    EventBusObserverBoundary<TKey>,
                    EventNotification<TKey>,
                    EventBusDeadLetter
                >
            >(),
            serviceProvider.GetRequiredService<EventBusEventIdentity<TKey>>()
        ));
        return services;
    }
}
