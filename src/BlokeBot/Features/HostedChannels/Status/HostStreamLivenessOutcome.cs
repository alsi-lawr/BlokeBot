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
        internal Unavailable(
            HostStreamLivenessUnavailableReason reason,
            Exception cause
        )
        {
            ArgumentNullException.ThrowIfNull(cause);
            Reason = reason;
            FailureType = cause.GetType().FullName ?? cause.GetType().Name;
            Cause = cause;
        }

        public HostStreamLivenessUnavailableReason Reason { get; }

        public string FailureType { get; }

        internal Exception Cause { get; }
    }
}
