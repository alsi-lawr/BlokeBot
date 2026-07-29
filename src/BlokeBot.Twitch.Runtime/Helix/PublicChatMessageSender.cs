using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace BlokeBot.Twitch.Runtime;

internal sealed class PublicChatMessageSender(PublicChatMessageQueue queue)
    : IPublicChatMessageSender
{
    public ValueTask<PublicChatSendOutcome> SendAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        CancellationToken cancellationToken
    )
    {
        return SendCoreAsync(channel, message, deadline, null, null, cancellationToken);
    }

    public ValueTask<PublicChatSendOutcome> SendAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        PublicChatPinIntent pinIntent,
        CancellationToken cancellationToken
    )
    {
        return SendCoreAsync(
            channel,
            message,
            deadline,
            null,
            pinIntent.Validate(),
            cancellationToken
        );
    }

    public ValueTask<PublicChatSendOutcome> SendCorrelatedAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        PublicChatDeliveryCorrelation correlation,
        CancellationToken cancellationToken
    )
    {
        return SendCoreAsync(
            channel,
            message,
            deadline,
            correlation.Validate(),
            null,
            cancellationToken
        );
    }

    public ValueTask<PublicChatSendOutcome> SendCorrelatedAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        PublicChatDeliveryCorrelation correlation,
        PublicChatPinIntent pinIntent,
        CancellationToken cancellationToken
    )
    {
        return SendCoreAsync(
            channel,
            message,
            deadline,
            correlation.Validate(),
            pinIntent.Validate(),
            cancellationToken
        );
    }

    private async ValueTask<PublicChatSendOutcome> SendCoreAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        PublicChatDeliveryCorrelation? correlation,
        PublicChatPinIntent? pinIntent,
        CancellationToken cancellationToken
    )
    {
        var outcome = await queue.EnqueueAsync(
            new PublicChatEnqueueCommand
            {
                Channel = channel,
                Message = message,
                Deadline = deadline,
                Correlation = correlation,
                PinIntent = pinIntent,
            },
            cancellationToken
        );
        switch (outcome)
        {
            case PublicChatEnqueueOutcome.Accepted:
                return new PublicChatSendOutcome.Accepted();
            case PublicChatEnqueueOutcome.Rejected:
                return new PublicChatSendOutcome.Rejected();
            case PublicChatEnqueueOutcome.SafePreEnqueueTransient transient:
                throw Rethrow(transient.Cause);
            case PublicChatEnqueueOutcome.Ambiguous ambiguous:
                throw Rethrow(ambiguous.Cause);
            case PublicChatEnqueueOutcome.Unexpected unexpected:
                throw Rethrow(unexpected.Cause);
            default:
                throw new UnreachableException("Unknown public-chat enqueue outcome.");
        }
    }

    private static Exception Rethrow(Exception exception)
    {
        ExceptionDispatchInfo.Capture(exception).Throw();
        throw new UnreachableException("Rethrow unexpectedly returned.");
    }
}
