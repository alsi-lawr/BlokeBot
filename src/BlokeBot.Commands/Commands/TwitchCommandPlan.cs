namespace BlokeBot.Commands;

internal sealed record TwitchCommandPlan
{
    public required IReadOnlyDictionary<string, TwitchCommandHandler> Routes { get; init; }

    public required IReadOnlyList<TwitchDynamicCommandHandler> DynamicHandlers { get; init; }

    public required IReadOnlyList<ITwitchCommandFilter> Filters { get; init; }

    public TwitchCommandHandler? FallbackHandler { get; init; }
}
