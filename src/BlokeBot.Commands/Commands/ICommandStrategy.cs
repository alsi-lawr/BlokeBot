namespace BlokeBot.Commands;

/// <summary>
/// Describes and executes one feature-owned command kind.
/// </summary>
public interface ICommandStrategy<TKind, TState>
    where TKind : notnull
{
    TKind Kind { get; }

    IReadOnlyList<string> DefaultAliases { get; }

    bool RequiresModerator { get; }

    ValueTask<string> ModeratorOnlyReplyAsync(
        CommandStrategyContext<TKind, TState> context,
        CancellationToken cancellationToken
    );

    ValueTask ExecuteAsync(
        CommandStrategyContext<TKind, TState> context,
        CancellationToken cancellationToken
    );
}

public sealed record CommandStrategyContext<TKind, TState>(
    TKind Kind,
    TState State,
    TwitchCommandContext Command,
    IReadOnlyList<string> Args
)
    where TKind : notnull;
