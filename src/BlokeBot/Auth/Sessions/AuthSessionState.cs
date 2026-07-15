using System.Diagnostics;
using BlokeBot.Hosts;

namespace BlokeBot.Auth.Sessions;

public abstract record AuthSessionState
{
    private AuthSessionState() { }

    public TResult Match<TResult>(
        Func<NoSelection, TResult> noSelection,
        Func<Selected, TResult> selected,
        Func<Invalid, TResult> invalid
    )
    {
        return this switch
        {
            NoSelection value => noSelection(value),
            Selected value => selected(value),
            Invalid value => invalid(value),
            _ => throw new UnreachableException("Unknown authenticated session state."),
        };
    }

    public sealed record NoSelection : AuthSessionState;

    public sealed record Selected(BotHostSelection Selection) : AuthSessionState;

    public sealed record Invalid : AuthSessionState;
}
