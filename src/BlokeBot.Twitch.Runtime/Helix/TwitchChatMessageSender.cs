using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchChatMessageSender(PublicChatMessageQueue queue)
    : ITwitchChatMessageSender
{
    public async Task SendAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        CancellationToken cancellationToken
    )
    {
        var outcome = await queue.EnqueueAsync(
            new PublicChatEnqueueCommand
            {
                Channel = channel,
                Message = message,
                Deadline = deadline,
            },
            cancellationToken
        );
        switch (outcome)
        {
            case PublicChatEnqueueOutcome.Accepted:
                return;
            case PublicChatEnqueueOutcome.Rejected:
                throw new InvalidOperationException("The public-chat message was rejected.");
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
