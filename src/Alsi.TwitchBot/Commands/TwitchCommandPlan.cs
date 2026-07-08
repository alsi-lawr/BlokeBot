namespace Alsi.TwitchBot;

internal sealed record TwitchCommandPlan(
    IReadOnlyDictionary<string, TwitchCommandHandler> routes,
    IReadOnlyList<Type> filters,
    TwitchCommandHandler? fallbackHandler
)
{
    public IReadOnlyDictionary<string, TwitchCommandHandler> Routes { get; } = routes;

    public IReadOnlyList<Type> Filters { get; } = filters;

    public TwitchCommandHandler? FallbackHandler { get; } = fallbackHandler;
}
