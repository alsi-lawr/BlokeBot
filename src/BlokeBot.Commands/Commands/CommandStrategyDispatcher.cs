namespace BlokeBot.Commands;

/// <summary>
/// Executes resolved feature command routes through typed strategies.
/// </summary>
public sealed class CommandStrategyDispatcher<TKind, TState>(
    CommandStrategyCatalog<TKind, TState> catalog
)
    where TKind : struct, Enum
{
    public async ValueTask<CommandHandlingOutcome> DispatchAsync(
        CommandRoute<TKind, TState> route,
        ChatCommandContext command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        var context = new CommandStrategyContext<TKind, TState>(
            route.Kind,
            route.State,
            command,
            args
        );
        return await catalog
            .Find(route.Kind)
            .Match(
                _ =>
                    ValueTask.FromResult<CommandHandlingOutcome>(
                        new CommandHandlingOutcome.Unhandled()
                    ),
                found => DispatchAsync(found.Strategy)
            );

        async ValueTask<CommandHandlingOutcome> DispatchAsync(
            ICommandStrategy<TKind, TState> strategy
        )
        {
            return await strategy.Access.Match(
                _ => ExecuteAsync(),
                moderatorOnly =>
                    ChatModeratorPolicy.IsModerator(command.Message)
                        ? ExecuteAsync()
                        : RejectModeratorOnlyAsync(moderatorOnly)
            );

            async ValueTask<CommandHandlingOutcome> ExecuteAsync()
            {
                await strategy.ExecuteAsync(context, cancellationToken);
                return new CommandHandlingOutcome.Handled();
            }

            async ValueTask<CommandHandlingOutcome> RejectModeratorOnlyAsync(
                CommandStrategyAccess<TKind, TState>.ModeratorOnly moderatorOnly
            )
            {
                var response = await moderatorOnly.Response(context, cancellationToken);
                if (!string.IsNullOrWhiteSpace(response.Message))
                {
                    await command.RespondAsync(response, cancellationToken);
                }

                return new CommandHandlingOutcome.Handled();
            }
        }
    }
}
