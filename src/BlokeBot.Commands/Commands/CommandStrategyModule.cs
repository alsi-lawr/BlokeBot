namespace BlokeBot.Commands;

/// <summary>
/// Connects dynamic chat command routes to typed feature strategies.
/// </summary>
public sealed class CommandStrategyModule<TKind, TState>(
    ICommandRouteResolver<TKind, TState> resolver,
    CommandStrategyDispatcher<TKind, TState> dispatcher
) : IChatCommandModule
    where TKind : struct, Enum
{
    public void AddCommands(IChatCommandBuilder commands)
    {
        commands.MapDynamic(RouteAsync);
    }

    private async ValueTask<bool> RouteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        var route = await resolver.ResolveAsync(context, cancellationToken);
        if (route is null)
        {
            return false;
        }

        var result = await dispatcher.DispatchAsync(route, context, args, cancellationToken);
        return result.Status == CommandStrategyDispatchStatus.Handled;
    }
}
