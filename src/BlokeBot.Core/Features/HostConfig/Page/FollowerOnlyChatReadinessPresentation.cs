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
    public static FollowerOnlyChatReadinessPresentation From(FollowerOnlyChatReadiness readiness) =>
        readiness.Match<FollowerOnlyChatReadinessPresentation>(
            static _ => new(FollowerOnlyChatSetupState.NotRequired, null),
            static _ => new(FollowerOnlyChatSetupState.Exempt, null),
            static _ => new(FollowerOnlyChatSetupState.Eligible, null),
            static waiting => new(FollowerOnlyChatSetupState.Waiting, waiting.EligibleAtUtc),
            static _ => new(FollowerOnlyChatSetupState.NotFollowing, null),
            static unavailable =>
                new(
                    unavailable.Failure
                    == FollowerOnlyChatVerificationFailure.MissingFollowReadScope
                        ? FollowerOnlyChatSetupState.ReconnectRequired
                        : FollowerOnlyChatSetupState.UnableToVerify,
                    null
                )
        );
}
