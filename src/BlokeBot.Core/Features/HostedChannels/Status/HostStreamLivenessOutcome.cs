using BlokeBot.Functional;

namespace BlokeBot.Core.Features.HostedChannels.Status;

public enum HostStreamLivenessUnavailableReason
{
    AppAccessTokenUnavailable,
    ProviderRequestFailed,
    ProviderResponseInvalid,
    ProviderTimedOut,
}

public interface IHostStreamLivenessProvider
{
    IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin);
}

public abstract record HostStreamLivenessOutcome
{
    private HostStreamLivenessOutcome() { }

    public sealed record Live(string StreamId) : HostStreamLivenessOutcome;

    public sealed record Offline : HostStreamLivenessOutcome;

    public sealed record Unavailable : HostStreamLivenessOutcome
    {
        internal Unavailable(HostStreamLivenessUnavailableReason reason, Exception cause)
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
