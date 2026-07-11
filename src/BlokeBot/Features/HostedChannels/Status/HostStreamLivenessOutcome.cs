namespace BlokeBot.Features.HostedChannels.Status;

public enum HostStreamLivenessUnavailableReason
{
    AppAccessTokenUnavailable,
    ProviderRequestFailed,
    ProviderResponseInvalid,
    ProviderTimedOut,
}

public abstract record HostStreamLivenessOutcome
{
    private HostStreamLivenessOutcome() { }

    public sealed record Live : HostStreamLivenessOutcome;

    public sealed record Offline : HostStreamLivenessOutcome;

    public sealed record Unavailable : HostStreamLivenessOutcome
    {
        public required HostStreamLivenessUnavailableReason Reason { get; init; }

        public required Exception Cause { get; init; }
    }
}
