namespace BlokeBot.Commands;

internal sealed record TwitchCommandPlan(
    IReadOnlyDictionary<string, TwitchCommandHandler> routes,
    IReadOnlyList<TwitchDynamicCommandHandler> dynamicHandlers,
    IReadOnlyList<Type> filters,
    TwitchCommandHandler? fallbackHandler
)
{
    public IReadOnlyDictionary<string, TwitchCommandHandler> Routes { get; } = routes;

    public IReadOnlyList<TwitchDynamicCommandHandler> DynamicHandlers { get; } = dynamicHandlers;

    public IReadOnlyList<Type> Filters { get; } = filters;

    public TwitchCommandHandler? FallbackHandler { get; } = fallbackHandler;
}
