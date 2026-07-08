namespace BlokeBot.Commands;

/// <summary>
/// Resolves a received chat command to a feature-owned command route.
/// </summary>
public interface ICommandRouteResolver<TKind, TState>
    where TKind : notnull
{
    ValueTask<CommandRoute<TKind, TState>?> ResolveAsync(
        TwitchCommandContext context,
        CancellationToken cancellationToken
    );
}
