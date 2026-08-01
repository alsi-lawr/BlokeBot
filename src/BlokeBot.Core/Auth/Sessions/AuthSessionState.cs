using BlokeBot.Core.Hosts;

namespace BlokeBot.Core.Auth.Sessions;

public abstract record AuthSessionState
{
    private AuthSessionState() { }

    public abstract TResult Match<TResult>(
        Func<NoSelection, TResult> noSelection,
        Func<Selected, TResult> selected,
        Func<Invalid, TResult> invalid
    );

    public sealed record NoSelection : AuthSessionState
    {
        public override TResult Match<TResult>(
            Func<NoSelection, TResult> noSelection,
            Func<Selected, TResult> selected,
            Func<Invalid, TResult> invalid
        ) => noSelection(this);
    }

    public sealed record Selected(BotHostSelection Selection) : AuthSessionState
    {
        public override TResult Match<TResult>(
            Func<NoSelection, TResult> noSelection,
            Func<Selected, TResult> selected,
            Func<Invalid, TResult> invalid
        ) => selected(this);
    }

    public sealed record Invalid : AuthSessionState
    {
        public override TResult Match<TResult>(
            Func<NoSelection, TResult> noSelection,
            Func<Selected, TResult> selected,
            Func<Invalid, TResult> invalid
        ) => invalid(this);
    }
}
