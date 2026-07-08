using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace BlokeBot.Commands;

internal sealed class TwitchCommandPlanBuilder : ITwitchCommandBuilder
{
    private readonly ConcurrentDictionary<string, TwitchCommandHandler> routes = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly List<Type> filters = [];
    private TwitchCommandHandler? fallbackHandler;

    public ITwitchCommandBuilder Map(string route, TwitchCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);

        routes[route.TrimStart('!')] = handler;
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
        filters.Add(typeof(TFilter));
        return this;
    }

    public TwitchCommandPlan Build() =>
        new(
            new ReadOnlyDictionary<string, TwitchCommandHandler>(
                routes.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
            ),
            Array.AsReadOnly<Type>([.. filters]),
            fallbackHandler
        );
}
