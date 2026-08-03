using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace BlokeBot.Commands;

internal sealed class ChatCommandPlanBuilder : IChatCommandBuilder
{
    private readonly Dictionary<Type, IChatCommandFilter> _availableFilters = [];
    private readonly ConcurrentDictionary<string, ChatCommandHandler> _routes = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly List<DynamicChatCommandHandler> _dynamicHandlers = [];
    private readonly List<IChatCommandFilter> _filters = [];
    private ChatCommandHandler? _fallbackHandler;

    public ChatCommandPlanBuilder(IEnumerable<IChatCommandFilter> registeredFilters)
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

    public IChatCommandBuilder Map(string route, ChatCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);

        var normalized = CommandAliasNormalizer.Normalize(route);
        return !_routes.TryAdd(normalized, handler)
            ? throw new InvalidOperationException(
                $"Command route '!{normalized}' was registered more than once."
            )
            : (IChatCommandBuilder)this;
    }

    public IChatCommandBuilder Map(FixedChatCommandRoute route, ChatCommandHandler handler) =>
        Map(route.Value, handler);

    public IChatCommandBuilder MapDynamic(DynamicChatCommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _dynamicHandlers.Add(handler);
        return this;
    }

    public IChatCommandBuilder MapFallback(ChatCommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _fallbackHandler = handler;
        return this;
    }

    public IChatCommandBuilder UseFilter<TFilter>()
        where TFilter : class, IChatCommandFilter
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

    public ChatCommandPlan Build() =>
        new()
        {
            Routes = new ReadOnlyDictionary<string, ChatCommandHandler>(
                _routes.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
            ),
            DynamicHandlers = Array.AsReadOnly<DynamicChatCommandHandler>([.. _dynamicHandlers]),
            Filters = Array.AsReadOnly<IChatCommandFilter>([.. _filters]),
            FallbackHandler = _fallbackHandler,
        };
}
