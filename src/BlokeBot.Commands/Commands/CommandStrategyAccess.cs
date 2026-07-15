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

    public abstract TResult Match<TResult>(
        Func<Everyone, TResult> everyone,
        Func<ModeratorOnly, TResult> moderatorOnly
    );

    public sealed record Everyone : CommandStrategyAccess<TKind, TState>
    {
        public override TResult Match<TResult>(
            Func<Everyone, TResult> everyone,
            Func<ModeratorOnly, TResult> moderatorOnly
        )
        {
            return everyone(this);
        }
    }

    public sealed record ModeratorOnly(ModeratorOnlyResponse<TKind, TState> Response)
        : CommandStrategyAccess<TKind, TState>
    {
        public override TResult Match<TResult>(
            Func<Everyone, TResult> everyone,
            Func<ModeratorOnly, TResult> moderatorOnly
        )
        {
            return moderatorOnly(this);
        }
    }
}
