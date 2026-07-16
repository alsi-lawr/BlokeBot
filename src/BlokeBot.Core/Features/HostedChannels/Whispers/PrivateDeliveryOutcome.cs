using System.Net;

namespace BlokeBot.Core.Features.HostedChannels.Whispers;

public sealed record PrivateDeliveryReceipt;

public abstract record PrivateDeliveryError
{
    private PrivateDeliveryError() { }

    public sealed record Disabled : PrivateDeliveryError;

    public sealed record SenderIdentityUnavailable : PrivateDeliveryError;

    public sealed record RecipientUnavailable : PrivateDeliveryError;

    public sealed record SelfRecipient : PrivateDeliveryError;

    public sealed record QuotaExceeded(WhisperQuotaStatus Status) : PrivateDeliveryError;

    public sealed record RateLimited(HttpStatusCode StatusCode) : PrivateDeliveryError;

    public sealed record Transient : PrivateDeliveryError
    {
        internal Transient(Exception cause)
        {
            ArgumentNullException.ThrowIfNull(cause);
            FailureType = cause.GetType().FullName ?? cause.GetType().Name;
            Cause = cause;
        }

        public string FailureType { get; }

        internal Exception Cause { get; }
    }

    public sealed record Rejected(HttpStatusCode StatusCode) : PrivateDeliveryError;

    public sealed record Ambiguous : PrivateDeliveryError
    {
        internal Ambiguous(Exception cause)
        {
            ArgumentNullException.ThrowIfNull(cause);
            FailureType = cause.GetType().FullName ?? cause.GetType().Name;
            Cause = cause;
        }

        public string FailureType { get; }

        internal Exception Cause { get; }
    }

    public sealed record Unexpected : PrivateDeliveryError
    {
        internal Unexpected(Exception cause)
        {
            ArgumentNullException.ThrowIfNull(cause);
            FailureType = cause.GetType().FullName ?? cause.GetType().Name;
            Cause = cause;
        }

        public string FailureType { get; }

        internal Exception Cause { get; }
    }
}
