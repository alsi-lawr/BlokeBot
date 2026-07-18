using BlokeBot.Core.Features.HostedChannels.Status;

namespace BlokeBot.Core.Features.HostConfig.Page;

internal enum FollowerOnlyChatSetupState
{
    NotRequired,
    Exempt,
    Eligible,
    Waiting,
    NotFollowing,
    ReconnectRequired,
    UnableToVerify,
}

internal sealed record FollowerOnlyChatReadinessPresentation(
    FollowerOnlyChatSetupState State,
    DateTimeOffset? EligibleAtUtc
)
{
    public static FollowerOnlyChatReadinessPresentation From(FollowerOnlyChatReadiness readiness)
    {
        return readiness.Match<FollowerOnlyChatReadinessPresentation>(
            _ => new(FollowerOnlyChatSetupState.NotRequired, null),
            _ => new(FollowerOnlyChatSetupState.Exempt, null),
            _ => new(FollowerOnlyChatSetupState.Eligible, null),
            waiting => new(FollowerOnlyChatSetupState.Waiting, waiting.EligibleAtUtc),
            _ => new(FollowerOnlyChatSetupState.NotFollowing, null),
            unavailable =>
                new(
                    unavailable.Failure
                    == FollowerOnlyChatVerificationFailure.MissingFollowReadScope
                        ? FollowerOnlyChatSetupState.ReconnectRequired
                        : FollowerOnlyChatSetupState.UnableToVerify,
                    null
                )
        );
    }
}
