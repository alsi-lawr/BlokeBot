using System.Diagnostics;

namespace BlokeBot.Core.Features.HostedChannels.Status;

public abstract record FollowerOnlyChatReadiness
{
    private FollowerOnlyChatReadiness() { }

    public TResult Match<TResult>(
        Func<NotRequired, TResult> notRequired,
        Func<Exempt, TResult> exempt,
        Func<EligibleNow, TResult> eligibleNow,
        Func<WaitingUntil, TResult> waitingUntil,
        Func<NotFollowing, TResult> notFollowing,
        Func<UnableToVerify, TResult> unableToVerify
    ) =>
        this switch
        {
            NotRequired value => notRequired(value),
            Exempt value => exempt(value),
            EligibleNow value => eligibleNow(value),
            WaitingUntil value => waitingUntil(value),
            NotFollowing value => notFollowing(value),
            UnableToVerify value => unableToVerify(value),
            _ => throw new UnreachableException("Unknown follower-only chat readiness."),
        };

    public sealed record NotRequired : FollowerOnlyChatReadiness;

    public sealed record Exempt(FollowerOnlyChatExemption Exemption) : FollowerOnlyChatReadiness;

    public sealed record EligibleNow : FollowerOnlyChatReadiness;

    public sealed record WaitingUntil(DateTimeOffset EligibleAtUtc) : FollowerOnlyChatReadiness;

    public sealed record NotFollowing : FollowerOnlyChatReadiness;

    public sealed record UnableToVerify(FollowerOnlyChatVerificationFailure Failure)
        : FollowerOnlyChatReadiness;
}

public enum FollowerOnlyChatExemption
{
    Broadcaster,
    Moderator,
}

public enum FollowerOnlyChatVerificationFailure
{
    ChannelLookupUnavailable,
    ChatSettingsUnavailable,
    BotTokenUnavailable,
    BotTokenInvalid,
    BotTokenUnknown,
    BotAccountMismatch,
    MissingModeratorCheckScope,
    ModeratorCheckUnavailable,
    MissingFollowReadScope,
    FollowReadUnavailable,
}
