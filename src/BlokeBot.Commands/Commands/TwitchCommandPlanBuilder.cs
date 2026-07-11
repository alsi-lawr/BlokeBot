using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace BlokeBot.Commands;

internal sealed class TwitchCommandPlanBuilder : ITwitchCommandBuilder
{
    private readonly Dictionary<Type, ITwitchCommandFilter> availableFilters = [];
    private readonly ConcurrentDictionary<string, TwitchCommandHandler> routes = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly List<TwitchDynamicCommandHandler> dynamicHandlers = [];
    private readonly List<ITwitchCommandFilter> filters = [];
    private TwitchCommandHandler? fallbackHandler;

    public TwitchCommandPlanBuilder(IEnumerable<ITwitchCommandFilter> registeredFilters)
    {
        ArgumentNullException.ThrowIfNull(registeredFilters);

        foreach (var filter in registeredFilters)
        {
            var filterType = filter.GetType();
            if (!availableFilters.TryAdd(filterType, filter))
            {
                throw new InvalidOperationException(
                    $"Command filter '{filterType.FullName}' was registered more than once."
                );
            }
        }
    }

    public ITwitchCommandBuilder Map(string route, TwitchCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);

        routes[route.TrimStart('!')] = handler;
        return this;
    }

    public ITwitchCommandBuilder MapDynamic(TwitchDynamicCommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        dynamicHandlers.Add(handler);
        return this;
    }

    public ITwitchCommandBuilder MapFallback(TwitchCommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        fallbackHandler = handler;
        return this;
    }

    public ITwitchCommandBuilder UseFilter<TFilter>()
        where TFilter : class, ITwitchCommandFilter
    {
        if (!availableFilters.TryGetValue(typeof(TFilter), out var filter))
        {
            throw new InvalidOperationException(
                $"Command filter '{typeof(TFilter).FullName}' must be registered explicitly."
            );
        }

        filters.Add(filter);
        return this;
    }

    public TwitchCommandPlan Build() =>
        new()
        {
            Routes = new ReadOnlyDictionary<string, TwitchCommandHandler>(
                routes.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
            ),
            DynamicHandlers = Array.AsReadOnly<TwitchDynamicCommandHandler>([.. dynamicHandlers]),
            Filters = Array.AsReadOnly<ITwitchCommandFilter>([.. filters]),
            FallbackHandler = fallbackHandler,
        };
}
