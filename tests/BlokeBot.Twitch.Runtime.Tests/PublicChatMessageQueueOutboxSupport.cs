using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;
using BlokeBot.Eventing;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public abstract partial class PublicChatMessageQueueTestBase
{
    private protected sealed class InMemoryOutbox(PublicChatRetryPolicy retryPolicy)
        : IPublicChatOutbox
    {
        private readonly object _gate = new();
        private readonly List<Row> _rows = [];
        private readonly List<Delivery> _deliveries = [];
        private readonly Channel<RowStatus> _completions = Channel.CreateUnbounded<RowStatus>();
        private readonly PublicChatRetryPolicy _safePreSendRetryPolicy =
            retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        private long _nextId = 1;

        public Action? AfterEnqueue { get; init; }

        public Exception? EnqueueFailure { get; init; }

        public int EnqueueCount { get; private set; }

        public PublicChatDeliveryDeadline? LastEnqueuedDeadline { get; private set; }

        public IReadOnlyList<string> PendingMessages
        {
            get
            {
                lock (_gate)
                {
                    return _rows
                        .Where(row => row.Status == RowStatus.Pending)
                        .Select(row => row.Message!)
                        .ToArray();
                }
            }
        }

        public OutboxSnapshot SingleSnapshot
        {
            get
            {
                lock (_gate)
                {
                    if (_rows.Count == 0)
                    {
                        return field.ShouldNotBeNull();
                    }

                    var row = _rows.ShouldHaveSingleItem();
                    return Snapshot(row);
                }
            }
            private set;
        }

        private static OutboxSnapshot Snapshot(Row row)
        {
            return new()
            {
                Status = row.Status,
                AttemptCount = row.AttemptCount,
                SafePreSendFailureCount = row.SafePreSendFailureCount,
                NextAttemptAt = row.NextAttemptAt,
                Message = row.Message,
            };
        }

        public ValueTask<RowStatus> ReadCompletionAsync()
        {
            return _completions.Reader.ReadAsync();
        }

        public ValueTask<PublicChatEnqueueOutcome> EnqueueAsync(
            PublicChatOutboxBatch batch,
            CancellationToken cancellationToken
        )
        {
            EnqueueCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (EnqueueFailure is { } failure)
            {
                throw failure;
            }

            long[] ids;
            lock (_gate)
            {
                LastEnqueuedDeadline = batch.Deadline;
                ids = batch
                    .Items.Select(item =>
                    {
                        var id = _nextId++;
                        _rows.Add(new Row(id, batch.Channel, item, batch.EnqueuedAt));
                        return id;
                    })
                    .ToArray();
            }

            AfterEnqueue?.Invoke();
            return ValueTask.FromResult<PublicChatEnqueueOutcome>(
                new PublicChatEnqueueOutcome.Accepted(
                    new PublicChatOutboxReceipt(ImmutableArray.Create(ids))
                )
            );
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
            lock (_gate)
            {
                var active = _rows.FirstOrDefault(row =>
                    row.Status is RowStatus.Claimed or RowStatus.Sending
                );
                if (active is not null)
                {
                    return ValueTask.FromResult<PublicChatClaimOutcome>(
                        new PublicChatClaimOutcome.AwaitingAvailability(active.ClaimExpiresAt)
                    );
                }

                var previousAttempt = _rows
                    .Where(row => row.CompletedAt is not null && row.AttemptCount > 0)
                    .Select(row => row.CompletedAt!.Value)
                    .DefaultIfEmpty(DateTimeOffset.MinValue)
                    .Append(
                        _deliveries
                            .Select(delivery => delivery.CompletedAt)
                            .DefaultIfEmpty(DateTimeOffset.MinValue)
                            .Max()
                    )
                    .Max();
                var claimable = _rows
                    .Where(row => row.Status is RowStatus.Pending or RowStatus.SafePreSendTransient)
                    .Select(row =>
                    {
                        var eligibleAt = row.NextAttemptAt;
                        if (previousAttempt != DateTimeOffset.MinValue)
                        {
                            eligibleAt = Max(eligibleAt, previousAttempt + sendInterval);
                        }

                        var previousDelivery = _deliveries
                            .Where(delivery =>
                                delivery.DeduplicationKey == row.Item.DeduplicationKey
                            )
                            .Select(delivery => delivery.CompletedAt)
                            .DefaultIfEmpty(DateTimeOffset.MinValue)
                            .Max();
                        if (previousDelivery != DateTimeOffset.MinValue)
                        {
                            eligibleAt = Max(eligibleAt, previousDelivery + duplicateCooldown);
                        }

                        return new Candidate(row, eligibleAt);
                    })
                    .OrderBy(candidate => candidate.EligibleAt)
                    .ThenBy(candidate => candidate.Row.EnqueuedAt)
                    .ThenBy(candidate => candidate.Row.Id)
                    .FirstOrDefault();
                if (claimable is null)
                {
                    return ValueTask.FromResult<PublicChatClaimOutcome>(
                        new PublicChatClaimOutcome.Empty()
                    );
                }

                if (claimable.EligibleAt > now)
                {
                    return ValueTask.FromResult<PublicChatClaimOutcome>(
                        new PublicChatClaimOutcome.AwaitingAvailability(claimable.EligibleAt)
                    );
                }

                var token = new PublicChatClaimToken(Guid.NewGuid());
                claimable.Row.Status = RowStatus.Claimed;
                claimable.Row.ClaimToken = token;
                claimable.Row.ClaimExpiresAt = claimExpiresAt;
                return ValueTask.FromResult<PublicChatClaimOutcome>(
                    new PublicChatClaimOutcome.Claimed(claimable.Row.Claimed(token))
                );
            }
        }

        public ValueTask<PublicChatClaimUpdate> BeginSendAsync(
            PublicChatClaimedMessage message,
            DateTimeOffset sendStartedAt,
            DateTimeOffset claimExpiresAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Claimed);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                row.Status = RowStatus.Sending;
                row.AttemptCount++;
                row.ClaimExpiresAt = claimExpiresAt;
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        public ValueTask<PublicChatClaimUpdate> RecordDeliveryOutcomeAsync(
            PublicChatClaimedMessage message,
            PublicChatDeliveryOutcome outcome,
            DateTimeOffset recordedAt,
            CancellationToken cancellationToken
        )
        {
            return outcome.Match(
                _ => DeleteSending(message, recordedAt, cancellationToken),
                _ =>
                    CompleteClaimedRedacted(
                        message,
                        RowStatus.MissingChannel,
                        recordedAt,
                        cancellationToken
                    ),
                _ =>
                    CompleteClaimedRedacted(
                        message,
                        RowStatus.MissingBot,
                        recordedAt,
                        cancellationToken
                    ),
                _ =>
                    CompleteClaimedRedacted(
                        message,
                        RowStatus.Unexpected,
                        recordedAt,
                        cancellationToken
                    ),
                _ => RecordSafePreSendTransient(message, recordedAt, cancellationToken),
                _ => CompleteSending(message, RowStatus.Rejected, recordedAt, cancellationToken),
                _ => CompleteSending(message, RowStatus.Ambiguous, recordedAt, cancellationToken),
                _ =>
                    CompleteClaimedRedacted(
                        message,
                        RowStatus.Unexpected,
                        recordedAt,
                        cancellationToken
                    )
            );
        }

        public ValueTask<PublicChatClaimUpdate> RecordPostBoundaryInterruptionAsync(
            PublicChatClaimedMessage message,
            PublicChatFailureDiagnostic.Send diagnostic,
            DateTimeOffset interruptedAt,
            CancellationToken cancellationToken
        )
        {
            return CompleteSending(message, RowStatus.Ambiguous, interruptedAt, cancellationToken);
        }

        public ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
            PublicChatClaimedMessage message,
            DateTimeOffset releasedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Claimed);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                row.Status =
                    row.SafePreSendFailureCount > 0
                        ? RowStatus.SafePreSendTransient
                        : RowStatus.Pending;
                row.ClaimToken = null;
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        public ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                IReadOnlyList<PublicChatPendingMessage> pending = _rows
                    .Where(row =>
                        row.Status == RowStatus.Sending
                        || (
                            row.ExpiresAt > now
                            && row.Status
                                is RowStatus.Pending
                                    or RowStatus.Claimed
                                    or RowStatus.SafePreSendTransient
                        )
                    )
                    .OrderBy(row => row.EnqueuedAt)
                    .ThenBy(row => row.Id)
                    .Select(row => new PublicChatPendingMessage(row.Channel, row.EnqueuedAt))
                    .ToArray();
                return ValueTask.FromResult(pending);
            }
        }

        private ValueTask<PublicChatClaimUpdate> CompleteSending(
            PublicChatClaimedMessage message,
            RowStatus status,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Sending);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                row.Status = status;
                row.CompletedAt = completedAt;
                row.Message = null;
                row.ClaimToken = null;
                NotifyCompletion(status);
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        private ValueTask<PublicChatClaimUpdate> DeleteSending(
            PublicChatClaimedMessage message,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Sending);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                row.Status = RowStatus.SentAndDeleted;
                row.CompletedAt = completedAt;
                row.Message = null;
                row.ClaimToken = null;
                SingleSnapshot = Snapshot(row);
                _deliveries.Add(new Delivery(row.Item.DeduplicationKey, completedAt));
                _rows.Remove(row);
                NotifyCompletion(RowStatus.SentAndDeleted);
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        private ValueTask<PublicChatClaimUpdate> RecordSafePreSendTransient(
            PublicChatClaimedMessage message,
            DateTimeOffset recordedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Claimed);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                var decision = PublicChatSafePreSendRetrySchedule.Create(
                    _safePreSendRetryPolicy,
                    new PublicChatSafePreSendFailureCount(row.SafePreSendFailureCount),
                    recordedAt
                );
                switch (decision)
                {
                    case PublicChatSafePreSendRetryDecision.Scheduled scheduled:
                        row.Status = RowStatus.SafePreSendTransient;
                        row.SafePreSendFailureCount = scheduled.FailureCount.Value;
                        row.NextAttemptAt = scheduled.NextAttemptAtUtc;
                        row.CompletedAt = null;
                        break;
                    case PublicChatSafePreSendRetryDecision.Exhausted exhausted:
                        row.Status = RowStatus.SafePreSendExhausted;
                        row.SafePreSendFailureCount = exhausted.FailureCount.Value;
                        row.NextAttemptAt = recordedAt;
                        row.CompletedAt = recordedAt;
                        row.Message = null;
                        break;
                    default:
                        throw new UnreachableException(
                            $"Unknown public chat safe pre-send retry decision {decision.GetType().Name}."
                        );
                }

                row.ClaimToken = null;
                NotifyCompletion(row.Status);
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        private ValueTask<PublicChatClaimUpdate> CompleteClaimedRedacted(
            PublicChatClaimedMessage message,
            RowStatus status,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken
        )
        {
            return CompleteClaimed(
                message,
                status,
                completedAt,
                static row => row.Message = null,
                cancellationToken
            );
        }

        private ValueTask<PublicChatClaimUpdate> CompleteClaimed(
            PublicChatClaimedMessage message,
            RowStatus status,
            DateTimeOffset completedAt,
            Action<Row> applyCase,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var row = Owned(message, RowStatus.Claimed);
                if (row is null)
                {
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );
                }

                row.Status = status;
                row.CompletedAt = completedAt;
                applyCase(row);
                row.ClaimToken = null;
                NotifyCompletion(status);
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        private Row? Owned(PublicChatClaimedMessage message, RowStatus status)
        {
            return _rows.SingleOrDefault(row =>
                row.Id == message.Id && row.Status == status && row.ClaimToken == message.ClaimToken
            );
        }

        private void NotifyCompletion(RowStatus status)
        {
            if (!_completions.Writer.TryWrite(status))
            {
                throw new InvalidOperationException(
                    "The public chat outcome could not be observed."
                );
            }
        }

        private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
        {
            return left >= right ? left : right;
        }

        private sealed class Row(
            long id,
            string channel,
            PublicChatOutboxItem item,
            DateTimeOffset enqueuedAt
        )
        {
            public long Id { get; } = id;

            public string Channel { get; } = channel;

            public PublicChatOutboxItem Item { get; } = item;

            public string? Message { get; set; } = item.Message;

            public DateTimeOffset EnqueuedAt { get; } = enqueuedAt;

            public DateTimeOffset ExpiresAt { get; } = enqueuedAt.AddMinutes(1);

            public DateTimeOffset NextAttemptAt { get; set; } = enqueuedAt;

            public RowStatus Status { get; set; }

            public int AttemptCount { get; set; }

            public int SafePreSendFailureCount { get; set; }

            public PublicChatClaimToken? ClaimToken { get; set; }

            public DateTimeOffset ClaimExpiresAt { get; set; }

            public DateTimeOffset? CompletedAt { get; set; }

            public PublicChatClaimedMessage Claimed(PublicChatClaimToken token)
            {
                return new()
                {
                    Id = Id,
                    Channel = Channel,
                    Message = Message!,
                    EnqueuedAt = EnqueuedAt,
                    ExpiresAt = ExpiresAt,
                    Attempt = AttemptCount + 1,
                    ClaimToken = token,
                    ClaimExpiresAt = ClaimExpiresAt,
                    DeduplicationKey = Item.DeduplicationKey,
                };
            }
        }

        private sealed record Candidate(Row Row, DateTimeOffset EligibleAt);

        private sealed record Delivery(
            PublicChatDeduplicationKey DeduplicationKey,
            DateTimeOffset CompletedAt
        );

        internal sealed record OutboxSnapshot
        {
            internal required RowStatus Status { get; init; }

            internal required int AttemptCount { get; init; }

            internal required int SafePreSendFailureCount { get; init; }

            internal required DateTimeOffset NextAttemptAt { get; init; }

            internal required string? Message { get; init; }
        }

        internal enum RowStatus
        {
            Pending,
            Claimed,
            Sending,
            SentAndDeleted,
            SafePreSendTransient,
            SafePreSendExhausted,
            MissingChannel,
            MissingBot,
            Rejected,
            Ambiguous,
            Unexpected,
        }
    }
}
