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

    private async ValueTask<CommandHandlingOutcome> RouteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        var resolution = await resolver.ResolveAsync(context, cancellationToken);
        return await resolution.Match(
            _ =>
                ValueTask.FromResult<CommandHandlingOutcome>(
                    new CommandHandlingOutcome.Unhandled()
                ),
            resolved => dispatcher.DispatchAsync(resolved.Route, context, args, cancellationToken)
        );
    }
}
