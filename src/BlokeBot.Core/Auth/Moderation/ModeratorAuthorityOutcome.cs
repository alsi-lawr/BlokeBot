using System.Diagnostics;

namespace BlokeBot.Core.Auth.Moderation;

public abstract record ModeratorAuthorityOutcome
{
    private ModeratorAuthorityOutcome() { }

    public TResult Match<TResult>(
        Func<Granted, TResult> granted,
        Func<Revoked, TResult> revoked,
        Func<HostMismatch, TResult> hostMismatch,
        Func<Unavailable, TResult> unavailable
    ) =>
        this switch
        {
            Granted value => granted(value),
            Revoked value => revoked(value),
            HostMismatch value => hostMismatch(value),
            Unavailable value => unavailable(value),
            _ => throw new UnreachableException("Unknown moderator authority outcome."),
        };

    public sealed record Granted : ModeratorAuthorityOutcome;

    public sealed record Revoked : ModeratorAuthorityOutcome;

    public sealed record HostMismatch : ModeratorAuthorityOutcome;

    public sealed record Unavailable : ModeratorAuthorityOutcome;
}
