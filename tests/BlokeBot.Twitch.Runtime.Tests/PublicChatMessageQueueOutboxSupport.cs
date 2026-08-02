using System.Collections.Concurrent;
using System.Threading.Channels;
using BlokeBot.Twitch.Runtime;

namespace BlokeBot.Twitch.Runtime.Tests;

public abstract partial class PublicChatMessageQueueTestBase
{
    private protected sealed class ScriptedOutbox : IPublicChatOutbox
    {
        private readonly ConcurrentQueue<PublicChatClaimUpdate> _beginSendScript = new();
        private readonly ConcurrentQueue<PublicChatClaimOutcome> _claimScript = new();
        private readonly ConcurrentQueue<
            IReadOnlyList<PublicChatPendingMessage>
        > _outstandingScript = new();
        private readonly Channel<BeginSendCall> _beginSendCalls =
            Channel.CreateUnbounded<BeginSendCall>();
        private readonly Channel<RecordDeliveryCall> _recordDeliveryCalls =
            Channel.CreateUnbounded<RecordDeliveryCall>();
        private int _nextSequence;

        public Func<
            PublicChatOutboxBatch,
            CancellationToken,
            ValueTask<PublicChatEnqueueOutcome>
        >? Enqueue { get; init; }

        public ConcurrentQueue<EnqueueCall> EnqueueCalls { get; } = new();

        public ConcurrentQueue<ClaimCall> ClaimCalls { get; } = new();

        public ConcurrentQueue<BeginSendCall> BeginSendCalls { get; } = new();

        public ConcurrentQueue<RecordDeliveryCall> RecordDeliveryCalls { get; } = new();

        public ConcurrentQueue<RecordInterruptionCall> RecordInterruptionCalls { get; } = new();

        public ConcurrentQueue<ReleaseCall> ReleaseCalls { get; } = new();

        public ConcurrentQueue<LoadOutstandingCall> LoadOutstandingCalls { get; } = new();

        public void ScriptClaims(params PublicChatClaimOutcome[] outcomes)
        {
            foreach (var outcome in outcomes)
            {
                _claimScript.Enqueue(outcome);
            }
        }

        public void ScriptBeginSend(params PublicChatClaimUpdate[] updates)
        {
            foreach (var update in updates)
            {
                _beginSendScript.Enqueue(update);
            }
        }

        public void ScriptOutstanding(params IReadOnlyList<PublicChatPendingMessage>[] snapshots)
        {
            foreach (var snapshot in snapshots)
            {
                _outstandingScript.Enqueue(snapshot);
            }
        }

        public ValueTask<BeginSendCall> ReadBeginSendAsync() => _beginSendCalls.Reader.ReadAsync();

        public ValueTask<RecordDeliveryCall> ReadRecordDeliveryAsync() =>
            _recordDeliveryCalls.Reader.ReadAsync();

        public async ValueTask<PublicChatEnqueueOutcome> EnqueueAsync(
            PublicChatOutboxBatch batch,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnqueueCalls.Enqueue(new(NextSequence(), batch, cancellationToken));
            if (Enqueue is { } enqueue)
            {
                return await enqueue(batch, cancellationToken);
            }

            return Accepted(batch.Items.Length);
        }

        public ValueTask<PublicChatClaimOutcome> TryClaimNextAsync(
            DateTimeOffset now,
            DateTimeOffset claimExpiresAt,
            TimeSpan sendInterval,
            TimeSpan duplicateCooldown,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = _claimScript.TryDequeue(out var scripted)
                ? scripted
                : new PublicChatClaimOutcome.Empty();
            ClaimCalls.Enqueue(
                new(
                    NextSequence(),
                    now,
                    claimExpiresAt,
                    sendInterval,
                    duplicateCooldown,
                    cancellationToken,
                    outcome
                )
            );
            return ValueTask.FromResult(outcome);
        }

        public ValueTask<PublicChatClaimUpdate> BeginSendAsync(
            PublicChatClaimedMessage message,
            DateTimeOffset sendStartedAt,
            DateTimeOffset claimExpiresAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var update = Next(_beginSendScript);
            var call = new BeginSendCall(
                NextSequence(),
                message,
                sendStartedAt,
                claimExpiresAt,
                cancellationToken,
                update
            );
            BeginSendCalls.Enqueue(call);
            Observe(_beginSendCalls, call);
            return ValueTask.FromResult(update);
        }

        public ValueTask<PublicChatClaimUpdate> RecordDeliveryOutcomeAsync(
            PublicChatClaimedMessage message,
            PublicChatDeliveryOutcome outcome,
            DateTimeOffset recordedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var call = new RecordDeliveryCall(
                NextSequence(),
                message,
                outcome,
                recordedAt,
                cancellationToken
            );
            RecordDeliveryCalls.Enqueue(call);
            Observe(_recordDeliveryCalls, call);
            return ValueTask.FromResult<PublicChatClaimUpdate>(new PublicChatClaimUpdate.Applied());
        }

        public ValueTask<PublicChatClaimUpdate> RecordPostBoundaryInterruptionAsync(
            PublicChatClaimedMessage message,
            PublicChatFailureDiagnostic.Send diagnostic,
            DateTimeOffset interruptedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecordInterruptionCalls.Enqueue(
                new(NextSequence(), message, diagnostic, interruptedAt, cancellationToken)
            );
            return ValueTask.FromResult<PublicChatClaimUpdate>(new PublicChatClaimUpdate.Applied());
        }

        public ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
            PublicChatClaimedMessage message,
            DateTimeOffset releasedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCalls.Enqueue(new(NextSequence(), message, releasedAt, cancellationToken));
            return ValueTask.FromResult<PublicChatClaimUpdate>(new PublicChatClaimUpdate.Applied());
        }

        public ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadOutstandingCalls.Enqueue(new(NextSequence(), now, cancellationToken));
            return ValueTask.FromResult<IReadOnlyList<PublicChatPendingMessage>>(
                _outstandingScript.TryDequeue(out var snapshot) ? snapshot : []
            );
        }

        private static PublicChatClaimUpdate Next(ConcurrentQueue<PublicChatClaimUpdate> script) =>
            script.TryDequeue(out var update) ? update : new PublicChatClaimUpdate.Applied();

        private static void Observe<TCall>(Channel<TCall> channel, TCall call)
        {
            if (!channel.Writer.TryWrite(call))
            {
                throw new InvalidOperationException("The outbox call could not be observed.");
            }
        }

        private int NextSequence() => Interlocked.Increment(ref _nextSequence);
    }

    private protected sealed record EnqueueCall(
        int Sequence,
        PublicChatOutboxBatch Batch,
        CancellationToken CancellationToken
    );

    private protected sealed record ClaimCall(
        int Sequence,
        DateTimeOffset Now,
        DateTimeOffset ClaimExpiresAt,
        TimeSpan SendInterval,
        TimeSpan DuplicateCooldown,
        CancellationToken CancellationToken,
        PublicChatClaimOutcome Outcome
    );

    private protected sealed record BeginSendCall(
        int Sequence,
        PublicChatClaimedMessage Message,
        DateTimeOffset SendStartedAt,
        DateTimeOffset ClaimExpiresAt,
        CancellationToken CancellationToken,
        PublicChatClaimUpdate Update
    );

    private protected sealed record RecordDeliveryCall(
        int Sequence,
        PublicChatClaimedMessage Message,
        PublicChatDeliveryOutcome Outcome,
        DateTimeOffset RecordedAt,
        CancellationToken CancellationToken
    );

    private protected sealed record RecordInterruptionCall(
        int Sequence,
        PublicChatClaimedMessage Message,
        PublicChatFailureDiagnostic.Send Diagnostic,
        DateTimeOffset InterruptedAt,
        CancellationToken CancellationToken
    );

    private protected sealed record ReleaseCall(
        int Sequence,
        PublicChatClaimedMessage Message,
        DateTimeOffset ReleasedAt,
        CancellationToken CancellationToken
    );

    private protected sealed record LoadOutstandingCall(
        int Sequence,
        DateTimeOffset Now,
        CancellationToken CancellationToken
    );
}
