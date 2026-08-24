namespace BlokeBot.Commands;

internal sealed record ChatCommandPlan
{
    public required IReadOnlyDictionary<string, DynamicChatCommandHandler> Routes { get; init; }

    public required IReadOnlyList<DynamicChatCommandHandler> DynamicHandlers { get; init; }

    public required IReadOnlyList<IChatCommandFilter> Filters { get; init; }

    public ChatCommandHandler? FallbackHandler { get; init; }
}
