namespace BlokeBot.Commands;

public abstract record CommandRouteResolution<TKind, TState>
    where TKind : notnull
{
    private CommandRouteResolution() { }

    public abstract TResult Match<TResult>(
        Func<Unresolved, TResult> unresolved,
        Func<Resolved, TResult> resolved
    );

    public sealed record Unresolved : CommandRouteResolution<TKind, TState>
    {
        public override TResult Match<TResult>(
            Func<Unresolved, TResult> unresolved,
            Func<Resolved, TResult> resolved
        ) => unresolved(this);
    }

    public sealed record Resolved(CommandRoute<TKind, TState> Route)
        : CommandRouteResolution<TKind, TState>
    {
        public override TResult Match<TResult>(
            Func<Unresolved, TResult> unresolved,
            Func<Resolved, TResult> resolved
        ) => resolved(this);
    }
}
