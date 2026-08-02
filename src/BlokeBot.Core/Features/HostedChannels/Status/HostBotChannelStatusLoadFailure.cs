using BlokeBot.Functional;

namespace BlokeBot.Core.Features.HostedChannels.Status;

public sealed record HostBotChannelStatusLoadFailure(
    string ModeratorStatusMessage,
    string FollowerReadStatusMessage
)
{
    public static Result<HostBotChannelStatus, HostBotChannelStatusLoadFailure> FromReadiness(
        HostBotReadinessOutcome readiness
    ) =>
        readiness is HostBotReadinessOutcome.Unknown or HostBotReadinessOutcome.IdentityLookupFailed
            ? Result<HostBotChannelStatus, HostBotChannelStatusLoadFailure>.Error(
                new(
                    "BlokeBot could not check whether the bot is a mod.",
                    "BlokeBot could not check follower-only giveaways."
                )
            )
            : Result<HostBotChannelStatus, HostBotChannelStatusLoadFailure>.Success(
                HostBotChannelStatus.FromReadiness(readiness)
            );
}
