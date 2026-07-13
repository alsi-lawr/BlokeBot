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

    async ValueTask<CommandResponse?> ModeratorOnlyResponseAsync(
        CommandStrategyContext<TKind, TState> context,
        CancellationToken cancellationToken
    )
    {
        var reply = await ModeratorOnlyReplyAsync(context, cancellationToken);
        return string.IsNullOrWhiteSpace(reply) ? null : CommandResponse.Chat(reply);
    }

    ValueTask ExecuteAsync(
        CommandStrategyContext<TKind, TState> context,
        CancellationToken cancellationToken
    );
}

public sealed record CommandStrategyContext<TKind, TState>(
    TKind Kind,
    TState State,
    ChatCommandContext Command,
    IReadOnlyList<string> Args
)
    where TKind : notnull;
