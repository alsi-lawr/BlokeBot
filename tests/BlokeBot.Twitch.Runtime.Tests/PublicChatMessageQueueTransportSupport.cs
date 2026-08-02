using System.Threading.Channels;

namespace BlokeBot.Twitch.Runtime.Tests;

public abstract partial class PublicChatMessageQueueTestBase
{
    private protected sealed class RecordingTransport : IPublicChatTransport
    {
        private readonly Channel<PublicChatClaimedMessage> _delivered =
            Channel.CreateUnbounded<PublicChatClaimedMessage>();

        public List<PublicChatClaimedMessage> Deliveries { get; } = [];

        public ValueTask<PublicChatPreparationOutcome> PrepareAsync(
            PublicChatClaimedMessage message,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PublicChatPreparationOutcome>(
                new PublicChatPreparationOutcome.Ready { Send = Prepared(message) }
            );

        public ValueTask<PublicChatTransportSendResult> SendAsync(
            PublicChatPreparedSend prepared,
            CancellationToken cancellationToken
        )
        {
            var message = prepared.Message;
            Deliveries.Add(message);
            if (!_delivered.Writer.TryWrite(message))
            {
                throw new InvalidOperationException(
                    "The transport delivery could not be observed."
                );
            }

            return ValueTask.FromResult<PublicChatTransportSendResult>(
                new PublicChatTransportSendResult.Sent()
            );
        }

        public ValueTask<PublicChatClaimedMessage> ReadAsync() => _delivered.Reader.ReadAsync();
    }

    private protected sealed class ScriptedTransport(
        Func<
            PublicChatClaimedMessage,
            CancellationToken,
            ValueTask<PublicChatPreparationOutcome>
        > prepare,
        Func<
            PublicChatPreparedSend,
            CancellationToken,
            ValueTask<PublicChatTransportSendResult>
        > send
    ) : IPublicChatTransport
    {
        public int PrepareCount { get; private set; }

        public int SendCount { get; private set; }

        public ValueTask<PublicChatPreparationOutcome> PrepareAsync(
            PublicChatClaimedMessage message,
            CancellationToken cancellationToken
        )
        {
            PrepareCount++;
            return prepare(message, cancellationToken);
        }

        public ValueTask<PublicChatTransportSendResult> SendAsync(
            PublicChatPreparedSend prepared,
            CancellationToken cancellationToken
        )
        {
            SendCount++;
            return send(prepared, cancellationToken);
        }
    }

    private protected sealed class RecordingQueueAlertObserver : IPublicChatQueueAlertObserver
    {
        private readonly Channel<PublicChatQueueBacklog> _alerts =
            Channel.CreateUnbounded<PublicChatQueueBacklog>();

        public List<PublicChatQueueBacklog> Alerts { get; } = [];

        public ValueTask QueueBackedUpAsync(
            PublicChatQueueBacklog backlog,
            CancellationToken cancellationToken
        )
        {
            Alerts.Add(backlog);
            if (!_alerts.Writer.TryWrite(backlog))
            {
                throw new InvalidOperationException("The queue alert could not be observed.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<PublicChatQueueBacklog> ReadAsync() => _alerts.Reader.ReadAsync();
    }

    private protected sealed class ThrowingQueueAlertObserver(string failureMessage)
        : IPublicChatQueueAlertObserver
    {
        public ValueTask QueueBackedUpAsync(
            PublicChatQueueBacklog backlog,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException(failureMessage);
    }
}
