using System.Diagnostics;

namespace BlokeBot.Twitch;

public abstract record ChatAnnouncementSendResult
{
    private ChatAnnouncementSendResult() { }

    public TResult Match<TResult>(
        Func<Sent, TResult> sent,
        Func<Invalid, TResult> invalid,
        Func<PermissionDenied, TResult> permissionDenied,
        Func<RateLimited, TResult> rateLimited,
        Func<Unexpected, TResult> unexpected,
        Func<Ambiguous, TResult> ambiguous
    )
    {
        return this switch
        {
            Sent value => sent(value),
            Invalid value => invalid(value),
            PermissionDenied value => permissionDenied(value),
            RateLimited value => rateLimited(value),
            Unexpected value => unexpected(value),
            Ambiguous value => ambiguous(value),
            _ => throw new UnreachableException("Unknown chat announcement send result."),
        };
    }

    public sealed record Sent : ChatAnnouncementSendResult;

    public sealed record Invalid : ChatAnnouncementSendResult;

    public sealed record PermissionDenied : ChatAnnouncementSendResult;

    public sealed record RateLimited : ChatAnnouncementSendResult;

    public sealed record Unexpected : ChatAnnouncementSendResult;

    public sealed record Ambiguous : ChatAnnouncementSendResult;
}
