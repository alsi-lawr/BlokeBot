using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Functional;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed class UnavailableCustomCommandStreamLivenessProvider : IHostStreamLivenessProvider
{
    public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
        IO<HostStreamLivenessOutcome, Never>.Create(_ =>
            ValueTask.FromResult(
                Result<HostStreamLivenessOutcome, Never>.Success(
                    new HostStreamLivenessOutcome.Unavailable(
                        HostStreamLivenessUnavailableReason.AppAccessTokenUnavailable,
                        new InvalidOperationException(
                            "Stream liveness is not configured for custom commands."
                        )
                    )
                )
            )
        );
}
