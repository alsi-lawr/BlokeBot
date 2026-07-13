using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace BlokeBot.Commands;

internal sealed class TwitchCommandPlanBuilder : ITwitchCommandBuilder
{
    private readonly Dictionary<Type, ITwitchCommandFilter> _availableFilters = [];
    private readonly ConcurrentDictionary<string, TwitchCommandHandler> _routes = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly List<TwitchDynamicCommandHandler> _dynamicHandlers = [];
    private readonly List<ITwitchCommandFilter> _filters = [];
    private TwitchCommandHandler? _fallbackHandler;

    public TwitchCommandPlanBuilder(IEnumerable<ITwitchCommandFilter> registeredFilters)
    {
        ArgumentNullException.ThrowIfNull(registeredFilters);

        foreach (var filter in registeredFilters)
        {
            var filterType = filter.GetType();
            if (!_availableFilters.TryAdd(filterType, filter))
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

        _routes[route.TrimStart('!')] = handler;
        return this;
    }

    public ITwitchCommandBuilder MapDynamic(TwitchDynamicCommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _dynamicHandlers.Add(handler);
        return this;
    }

    public ITwitchCommandBuilder MapFallback(TwitchCommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _fallbackHandler = handler;
        return this;
    }

    public ITwitchCommandBuilder UseFilter<TFilter>()
        where TFilter : class, ITwitchCommandFilter
    {
        if (!_availableFilters.TryGetValue(typeof(TFilter), out var filter))
        {
            throw new InvalidOperationException(
                $"Command filter '{typeof(TFilter).FullName}' must be registered explicitly."
            );
        }

        _filters.Add(filter);
        return this;
    }

    public TwitchCommandPlan Build()
    {
        return new()
        {
            Routes = new ReadOnlyDictionary<string, TwitchCommandHandler>(
                _routes.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
            ),
            DynamicHandlers = Array.AsReadOnly<TwitchDynamicCommandHandler>([.. _dynamicHandlers]),
            Filters = Array.AsReadOnly<ITwitchCommandFilter>([.. _filters]),
            FallbackHandler = _fallbackHandler,
        };
    }
}
