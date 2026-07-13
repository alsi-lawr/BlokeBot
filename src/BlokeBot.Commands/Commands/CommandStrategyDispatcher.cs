namespace BlokeBot.Commands;

/// <summary>
/// Executes resolved feature command routes through typed strategies.
/// </summary>
public sealed class CommandStrategyDispatcher<TKind, TState>(
    CommandStrategyCatalog<TKind, TState> catalog
)
    where TKind : struct, Enum
{
    public async ValueTask<CommandStrategyDispatchResult<TKind>> DispatchAsync(
        CommandRoute<TKind, TState> route,
        ChatCommandContext command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        var strategy = catalog.Find(route.Kind);
        if (strategy is null)
        {
            return CommandStrategyDispatchResult<TKind>.Unknown();
        }

        var context = new CommandStrategyContext<TKind, TState>(
            route.Kind,
            route.State,
            command,
            args
        );
        if (strategy.RequiresModerator && !ChatModeratorPolicy.IsModerator(command.Message))
        {
            var response = await strategy.ModeratorOnlyResponseAsync(context, cancellationToken);
            if (!string.IsNullOrWhiteSpace(response?.Message))
            {
                await command.RespondAsync(response, cancellationToken);
            }

            return CommandStrategyDispatchResult<TKind>.Handled(route.Kind);
        }

        await strategy.ExecuteAsync(context, cancellationToken);
        return CommandStrategyDispatchResult<TKind>.Handled(route.Kind);
    }
}

public enum CommandStrategyDispatchStatus
{
    Unknown,
    Handled,
}

public sealed record CommandStrategyDispatchResult<TKind>(
    CommandStrategyDispatchStatus Status,
    TKind? Kind
)
    where TKind : struct, Enum
{
    public static CommandStrategyDispatchResult<TKind> Unknown()
    {
        return new(CommandStrategyDispatchStatus.Unknown, null);
    }

    public static CommandStrategyDispatchResult<TKind> Handled(TKind kind)
    {
        return new(CommandStrategyDispatchStatus.Handled, kind);
    }
}
