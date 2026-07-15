using System.Diagnostics;

namespace BlokeBot.Commands;

public delegate ValueTask<CommandResponse> ModeratorOnlyResponse<TKind, TState>(
    CommandStrategyContext<TKind, TState> context,
    CancellationToken cancellationToken
)
    where TKind : notnull;

public abstract record CommandStrategyAccess<TKind, TState>
    where TKind : notnull
{
    private CommandStrategyAccess() { }

    public TResult Match<TResult>(
        Func<Everyone, TResult> everyone,
        Func<ModeratorOnly, TResult> moderatorOnly
    )
    {
        return this switch
        {
            Everyone value => everyone(value),
            ModeratorOnly value => moderatorOnly(value),
            _ => throw new UnreachableException("Unknown command strategy access."),
        };
    }

    public sealed record Everyone : CommandStrategyAccess<TKind, TState>;

    public sealed record ModeratorOnly(ModeratorOnlyResponse<TKind, TState> Response)
        : CommandStrategyAccess<TKind, TState>;
}
