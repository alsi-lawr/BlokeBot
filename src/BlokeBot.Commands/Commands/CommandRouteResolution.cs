using System.Diagnostics;

namespace BlokeBot.Commands;

public abstract record CommandRouteResolution<TKind, TState>
    where TKind : notnull
{
    private CommandRouteResolution() { }

    public TResult Match<TResult>(
        Func<Unresolved, TResult> unresolved,
        Func<Resolved, TResult> resolved
    )
    {
        return this switch
        {
            Unresolved value => unresolved(value),
            Resolved value => resolved(value),
            _ => throw new UnreachableException("Unknown command route resolution."),
        };
    }

    public sealed record Unresolved : CommandRouteResolution<TKind, TState>;

    public sealed record Resolved(CommandRoute<TKind, TState> Route)
        : CommandRouteResolution<TKind, TState>;
}
