using System.Diagnostics;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.PublicChat;

internal sealed class EfPublicChatOutbox(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PublicChatRetryPolicy retryPolicy
) : IPublicChatOutbox
{
    private static readonly TimeSpan ClaimAvailabilityPoll = TimeSpan.FromMilliseconds(250);
    private readonly PublicChatRetryPolicy safePreSendRetryPolicy =
        retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));

    public async ValueTask<PublicChatOutboxReceipt> EnqueueAsync(
        PublicChatOutboxBatch batch,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batch.Channel);
        if (
            batch.Items.IsDefaultOrEmpty
            || batch.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.Message)
                || string.IsNullOrWhiteSpace(item.DeduplicationKey.Value)
            )
        )
        {
            throw new ArgumentException("At least one non-blank message is required.", nameof(batch));
        }

        var createdAtUtc = batch.EnqueuedAt.UtcDateTime;
        var rows = batch.Items
            .Select(item =>
                new PublicChatOutboxMessage
                {
                    Channel = batch.Channel,
                    Message = item.Message,
                    DeduplicationKey = item.DeduplicationKey.Value,
                    CreatedAtUtc = createdAtUtc,
                    NextAttemptAtUtc = createdAtUtc,
                }
            )
            .ToArray();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.PublicChatOutboxMessages.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return new PublicChatOutboxReceipt([.. rows.Select(row => row.Id)]);
    }

    public async ValueTask<PublicChatClaimOutcome> TryClaimNextAsync(
        DateTimeOffset now,
        DateTimeOffset claimExpiresAt,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await TryClaimNextCoreAsync(
                now,
                claimExpiresAt,
                sendInterval,
                duplicateCooldown,
                cancellationToken
            );
        }
        catch (Exception exception)
            when (IsSqliteContention(exception) || IsClaimSlotContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimOutcome.Contended();
        }
    }

    public ValueTask<PublicChatClaimUpdate> BeginSendAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset sendStartedAt,
        DateTimeOffset claimExpiresAt,
        CancellationToken cancellationToken
    ) =>
        ExecuteStateTransitionAsync(
            (db, ct) =>
                db.PublicChatOutboxMessages
                    .Where(row =>
                        row.Id == message.Id
                        && row.Status == PublicChatOutboxStatus.Claimed
                        && row.ClaimToken == message.ClaimToken.Value
                        && row.ClaimExpiresAtUtc > sendStartedAt.UtcDateTime
                    )
                    .ExecuteUpdateAsync(
                        update =>
                            update
                                .SetProperty(
                                    row => row.Status,
                                    PublicChatOutboxStatus.Sending
                                )
                                .SetProperty(
                                    row => row.SendStartedAtUtc,
                                    sendStartedAt.UtcDateTime
                                )
                                .SetProperty(
                                    row => row.ClaimExpiresAtUtc,
                                    claimExpiresAt.UtcDateTime
                                )
                                .SetProperty(
                                    row => row.AttemptCount,
                                    row => row.AttemptCount + 1
                                )
                                .SetProperty(
                                    row => row.FailurePhase,
                                    (PublicChatOutboxFailurePhase?)null
                                )
                                .SetProperty(row => row.FailureType, (string?)null)
                                .SetProperty(row => row.HttpStatusCode, (int?)null)
                                .SetProperty(row => row.RejectionCode, (string?)null),
                        ct
                    ),
            cancellationToken
        );

    public ValueTask<PublicChatClaimUpdate> RecordDeliveryOutcomeAsync(
        PublicChatClaimedMessage message,
        PublicChatDeliveryOutcome outcome,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Match(
            _ => RecordDeliveredAsync(message, recordedAt, cancellationToken),
            transient =>
                RecordSafePreSendTransientAsync(
                    message,
                    transient.Diagnostic,
                    recordedAt,
                    cancellationToken
                ),
            rejection =>
                RecordRejectionAsync(
                    message,
                    rejection.Reason,
                    recordedAt,
                    cancellationToken
                ),
            ambiguous =>
                RecordAmbiguousAsync(
                    message,
                    ambiguous.Diagnostic,
                    recordedAt,
                    cancellationToken
                ),
            unexpected =>
                RecordUnexpectedAsync(
                    message,
                    unexpected.Diagnostic,
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
        ArgumentNullException.ThrowIfNull(diagnostic);
        return RecordAmbiguousAsync(
            message,
            diagnostic,
            interruptedAt,
            cancellationToken
        );
    }

    public async ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var initialClaimReleased = await db
                .PublicChatOutboxMessages.Where(row =>
                    row.Id == message.Id
                    && row.Status == PublicChatOutboxStatus.Claimed
                    && row.ClaimToken == message.ClaimToken.Value
                    && row.SafePreSendFailureCount == 0
                )
                .ExecuteUpdateAsync(
                    update =>
                        update
                            .SetProperty(
                                row => row.Status,
                                PublicChatOutboxStatus.Pending
                            )
                            .SetProperty(row => row.ClaimToken, (Guid?)null)
                            .SetProperty(row => row.ClaimSlot, (int?)null)
                            .SetProperty(
                                row => row.ClaimExpiresAtUtc,
                                (DateTime?)null
                            ),
                    cancellationToken
                );
            if (initialClaimReleased == 1)
                return new PublicChatClaimUpdate.Applied();

            var retryClaimReleased = await db
                .PublicChatOutboxMessages.Where(row =>
                    row.Id == message.Id
                    && row.Status == PublicChatOutboxStatus.Claimed
                    && row.ClaimToken == message.ClaimToken.Value
                    && row.SafePreSendFailureCount > 0
                )
                .ExecuteUpdateAsync(
                    update =>
                        update
                            .SetProperty(
                                row => row.Status,
                                PublicChatOutboxStatus.SafePreSendTransient
                            )
                            .SetProperty(row => row.ClaimToken, (Guid?)null)
                            .SetProperty(row => row.ClaimSlot, (int?)null)
                            .SetProperty(
                                row => row.ClaimExpiresAtUtc,
                                (DateTime?)null
                            ),
                    cancellationToken
                );
            return Changed(retryClaimReleased);
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

    public async ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                row.Status == PublicChatOutboxStatus.Pending
                || row.Status == PublicChatOutboxStatus.Claimed
                || row.Status == PublicChatOutboxStatus.Sending
                || row.Status == PublicChatOutboxStatus.SafePreSendTransient
            )
            .OrderBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Channel, row.CreatedAtUtc })
            .ToArrayAsync(cancellationToken);
        return rows
            .Select(row =>
                new PublicChatPendingMessage(row.Channel, ToDateTimeOffset(row.CreatedAtUtc))
            )
            .ToArray();
    }

    private async ValueTask<PublicChatClaimOutcome> TryClaimNextCoreAsync(
        DateTimeOffset now,
        DateTimeOffset claimExpiresAt,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown,
        CancellationToken cancellationToken
    )
    {
        var nowUtc = now.UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await RecoverExpiredAsync(db, nowUtc, cancellationToken);
        await ExhaustConfiguredSafePreSendRetriesAsync(db, nowUtc, cancellationToken);

        var activeClaim = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                (row.Status == PublicChatOutboxStatus.Claimed
                    || row.Status == PublicChatOutboxStatus.Sending)
                && row.ClaimExpiresAtUtc > nowUtc
            )
            .OrderBy(row => row.ClaimExpiresAtUtc)
            .Select(row => row.ClaimExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeClaim is { } activeClaimExpiry)
        {
            return new PublicChatClaimOutcome.AwaitingAvailability(
                Min(
                    ToDateTimeOffset(activeClaimExpiry),
                    now + ClaimAvailabilityPoll
                )
            );
        }

        var claimable = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                row.Status == PublicChatOutboxStatus.Pending
                || (
                    row.Status == PublicChatOutboxStatus.SafePreSendTransient
                    && row.SafePreSendFailureCount < safePreSendRetryPolicy.AttemptLimit
                )
            )
            .ToArrayAsync(cancellationToken);
        if (claimable.Length == 0)
            return new PublicChatClaimOutcome.Empty();

        var previousAttemptAt = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row => row.SendStartedAtUtc != null && row.CompletedAtUtc != null)
            .OrderByDescending(row => row.CompletedAtUtc)
            .Select(row => row.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var previousDeliveries = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                row.Status == PublicChatOutboxStatus.Delivered
                && row.CompletedAtUtc != null
            )
            .Select(row => new { row.DeduplicationKey, row.CompletedAtUtc })
            .ToArrayAsync(cancellationToken);
        var deliveredAtByKey = previousDeliveries
            .GroupBy(row => row.DeduplicationKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Max(row => row.CompletedAtUtc)!.Value,
                StringComparer.Ordinal
            );
        var candidate = claimable
            .Select(row =>
                new ClaimCandidate(
                    row,
                    EligibleAt(
                        row,
                        previousAttemptAt,
                        deliveredAtByKey,
                        sendInterval,
                        duplicateCooldown
                    )
                )
            )
            .OrderBy(value => value.EligibleAtUtc)
            .ThenBy(value => value.Row.CreatedAtUtc)
            .ThenBy(value => value.Row.Id)
            .First();
        if (candidate.EligibleAtUtc > nowUtc)
        {
            return new PublicChatClaimOutcome.AwaitingAvailability(
                ToDateTimeOffset(candidate.EligibleAtUtc)
            );
        }

        var claimToken = Guid.NewGuid();
        var changed = await db
            .PublicChatOutboxMessages.Where(row =>
                row.Id == candidate.Row.Id
                && (
                    row.Status == PublicChatOutboxStatus.Pending
                    || (
                        row.Status == PublicChatOutboxStatus.SafePreSendTransient
                        && row.SafePreSendFailureCount
                            < safePreSendRetryPolicy.AttemptLimit
                    )
                )
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(row => row.Status, PublicChatOutboxStatus.Claimed)
                        .SetProperty(row => row.ClaimToken, claimToken)
                        .SetProperty(row => row.ClaimSlot, 1)
                        .SetProperty(
                            row => row.ClaimExpiresAtUtc,
                            claimExpiresAt.UtcDateTime
                        ),
                cancellationToken
            );
        if (changed == 0)
            return new PublicChatClaimOutcome.Contended();

        var claimed = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .SingleOrDefaultAsync(row =>
                row.Id == candidate.Row.Id && row.ClaimToken == claimToken,
                cancellationToken
            );
        if (claimed is null)
        {
            throw new UnreachableException(
                "A successfully claimed public chat outbox row disappeared."
            );
        }

        return new PublicChatClaimOutcome.Claimed(MapClaimed(claimed));
    }

    private static async Task RecoverExpiredAsync(
        BlokeBotDbContext db,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        await db
            .PublicChatOutboxMessages.Where(row =>
                row.Status == PublicChatOutboxStatus.Sending
                && row.ClaimExpiresAtUtc <= nowUtc
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(
                            row => row.Status,
                            PublicChatOutboxStatus.Ambiguous
                        )
                        .SetProperty(row => row.Message, (string?)null)
                        .SetProperty(row => row.ClaimToken, (Guid?)null)
                        .SetProperty(row => row.ClaimSlot, (int?)null)
                        .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null)
                        .SetProperty(row => row.CompletedAtUtc, nowUtc)
                        .SetProperty(
                            row => row.FailurePhase,
                            PublicChatOutboxFailurePhase.Send
                        )
                        .SetProperty(
                            row => row.FailureType,
                            typeof(PublicChatSendLeaseExpired).FullName
                        ),
                cancellationToken
            );
        await db
            .PublicChatOutboxMessages.Where(row =>
                row.Status == PublicChatOutboxStatus.Claimed
                && row.ClaimExpiresAtUtc <= nowUtc
                && row.SafePreSendFailureCount == 0
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(row => row.Status, PublicChatOutboxStatus.Pending)
                        .SetProperty(row => row.ClaimToken, (Guid?)null)
                        .SetProperty(row => row.ClaimSlot, (int?)null)
                        .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken
            );
        await db
            .PublicChatOutboxMessages.Where(row =>
                row.Status == PublicChatOutboxStatus.Claimed
                && row.ClaimExpiresAtUtc <= nowUtc
                && row.SafePreSendFailureCount > 0
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(
                            row => row.Status,
                            PublicChatOutboxStatus.SafePreSendTransient
                        )
                        .SetProperty(row => row.ClaimToken, (Guid?)null)
                        .SetProperty(row => row.ClaimSlot, (int?)null)
                        .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken
            );
    }

    private Task ExhaustConfiguredSafePreSendRetriesAsync(
        BlokeBotDbContext db,
        DateTime nowUtc,
        CancellationToken cancellationToken
    ) =>
        db.PublicChatOutboxMessages
            .Where(row =>
                row.Status == PublicChatOutboxStatus.SafePreSendTransient
                && row.SafePreSendFailureCount >= safePreSendRetryPolicy.AttemptLimit
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(
                            row => row.Status,
                            PublicChatOutboxStatus.SafePreSendExhausted
                        )
                        .SetProperty(row => row.Message, (string?)null)
                        .SetProperty(row => row.NextAttemptAtUtc, nowUtc)
                        .SetProperty(row => row.CompletedAtUtc, nowUtc),
                cancellationToken
            );

    private ValueTask<PublicChatClaimUpdate> RecordDeliveredAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken
    ) =>
        ExecuteStateTransitionAsync(
            (db, ct) =>
                db.PublicChatOutboxMessages
                    .Where(row =>
                        row.Id == message.Id
                        && row.Status == PublicChatOutboxStatus.Sending
                        && row.ClaimToken == message.ClaimToken.Value
                    )
                    .ExecuteUpdateAsync(
                        update =>
                            update
                                .SetProperty(
                                    row => row.Status,
                                    PublicChatOutboxStatus.Delivered
                                )
                                .SetProperty(row => row.Message, (string?)null)
                                .SetProperty(row => row.ClaimToken, (Guid?)null)
                                .SetProperty(row => row.ClaimSlot, (int?)null)
                                .SetProperty(
                                    row => row.ClaimExpiresAtUtc,
                                    (DateTime?)null
                                )
                                .SetProperty(
                                    row => row.CompletedAtUtc,
                                    completedAt.UtcDateTime
                                ),
                        ct
                    ),
            cancellationToken
        );

    private async ValueTask<PublicChatClaimUpdate> RecordSafePreSendTransientAsync(
        PublicChatClaimedMessage message,
        PublicChatFailureDiagnostic.Preparation diagnostic,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var previousFailureCount = await db
                .PublicChatOutboxMessages.AsNoTracking()
                .Where(row =>
                    row.Id == message.Id
                    && row.Status == PublicChatOutboxStatus.Claimed
                    && row.ClaimToken == message.ClaimToken.Value
                )
                .Select(row => (int?)row.SafePreSendFailureCount)
                .SingleOrDefaultAsync(cancellationToken);
            if (previousFailureCount is not { } persistedFailureCount)
                return new PublicChatClaimUpdate.OwnershipLost();

            var decision = PublicChatSafePreSendRetrySchedule.Create(
                safePreSendRetryPolicy,
                new PublicChatSafePreSendFailureCount(persistedFailureCount),
                recordedAt
            );
            return decision switch
            {
                PublicChatSafePreSendRetryDecision.Scheduled scheduled => Changed(
                    await db
                        .PublicChatOutboxMessages.Where(row =>
                            row.Id == message.Id
                            && row.Status == PublicChatOutboxStatus.Claimed
                            && row.ClaimToken == message.ClaimToken.Value
                            && row.SafePreSendFailureCount == persistedFailureCount
                        )
                        .ExecuteUpdateAsync(
                            update =>
                                update
                                    .SetProperty(
                                        row => row.Status,
                                        PublicChatOutboxStatus.SafePreSendTransient
                                    )
                                    .SetProperty(
                                        row => row.SafePreSendFailureCount,
                                        scheduled.FailureCount.Value
                                    )
                                    .SetProperty(
                                        row => row.NextAttemptAtUtc,
                                        scheduled.NextAttemptAtUtc.UtcDateTime
                                    )
                                    .SetProperty(row => row.ClaimToken, (Guid?)null)
                                    .SetProperty(row => row.ClaimSlot, (int?)null)
                                    .SetProperty(
                                        row => row.ClaimExpiresAtUtc,
                                        (DateTime?)null
                                    )
                                    .SetProperty(
                                        row => row.CompletedAtUtc,
                                        (DateTime?)null
                                    )
                                    .SetProperty(
                                        row => row.FailurePhase,
                                        PublicChatOutboxFailurePhase.Preparation
                                    )
                                    .SetProperty(
                                        row => row.FailureType,
                                        diagnostic.FailureType.Value
                                    )
                                    .SetProperty(
                                        row => row.HttpStatusCode,
                                        HttpStatusCode(diagnostic.HttpStatus)
                                    )
                                    .SetProperty(
                                        row => row.RejectionCode,
                                        (string?)null
                                    ),
                            cancellationToken
                        )
                ),
                PublicChatSafePreSendRetryDecision.Exhausted exhausted => Changed(
                    await db
                        .PublicChatOutboxMessages.Where(row =>
                            row.Id == message.Id
                            && row.Status == PublicChatOutboxStatus.Claimed
                            && row.ClaimToken == message.ClaimToken.Value
                            && row.SafePreSendFailureCount == persistedFailureCount
                        )
                        .ExecuteUpdateAsync(
                            update =>
                                update
                                    .SetProperty(
                                        row => row.Status,
                                        PublicChatOutboxStatus.SafePreSendExhausted
                                    )
                                    .SetProperty(row => row.Message, (string?)null)
                                    .SetProperty(
                                        row => row.SafePreSendFailureCount,
                                        exhausted.FailureCount.Value
                                    )
                                    .SetProperty(
                                        row => row.NextAttemptAtUtc,
                                        recordedAt.UtcDateTime
                                    )
                                    .SetProperty(row => row.ClaimToken, (Guid?)null)
                                    .SetProperty(row => row.ClaimSlot, (int?)null)
                                    .SetProperty(
                                        row => row.ClaimExpiresAtUtc,
                                        (DateTime?)null
                                    )
                                    .SetProperty(
                                        row => row.SendStartedAtUtc,
                                        (DateTime?)null
                                    )
                                    .SetProperty(
                                        row => row.CompletedAtUtc,
                                        recordedAt.UtcDateTime
                                    )
                                    .SetProperty(
                                        row => row.FailurePhase,
                                        PublicChatOutboxFailurePhase.Preparation
                                    )
                                    .SetProperty(
                                        row => row.FailureType,
                                        diagnostic.FailureType.Value
                                    )
                                    .SetProperty(
                                        row => row.HttpStatusCode,
                                        HttpStatusCode(diagnostic.HttpStatus)
                                    )
                                    .SetProperty(
                                        row => row.RejectionCode,
                                        (string?)null
                                    ),
                            cancellationToken
                        )
                ),
                _ => throw new UnreachableException(
                    $"Unknown public chat safe pre-send retry decision {decision.GetType().Name}."
                ),
            };
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

    private ValueTask<PublicChatClaimUpdate> RecordRejectionAsync(
        PublicChatClaimedMessage message,
        PublicChatRejectionReason reason,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    )
    {
        var rejectionCode = reason.Match<string?>(code => code.Value, () => null);
        return ExecuteStateTransitionAsync(
            (db, ct) =>
                db.PublicChatOutboxMessages
                    .Where(row =>
                        row.Id == message.Id
                        && row.Status == PublicChatOutboxStatus.Sending
                        && row.ClaimToken == message.ClaimToken.Value
                    )
                    .ExecuteUpdateAsync(
                        update =>
                            update
                                .SetProperty(
                                    row => row.Status,
                                    PublicChatOutboxStatus.Rejected
                                )
                                .SetProperty(row => row.Message, (string?)null)
                                .SetProperty(row => row.ClaimToken, (Guid?)null)
                                .SetProperty(row => row.ClaimSlot, (int?)null)
                                .SetProperty(
                                    row => row.ClaimExpiresAtUtc,
                                    (DateTime?)null
                                )
                                .SetProperty(
                                    row => row.CompletedAtUtc,
                                    recordedAt.UtcDateTime
                                )
                                .SetProperty(
                                    row => row.FailurePhase,
                                    PublicChatOutboxFailurePhase.Send
                                )
                                .SetProperty(
                                    row => row.RejectionCode,
                                    rejectionCode
                                ),
                        ct
                    ),
            cancellationToken
        );
    }

    private ValueTask<PublicChatClaimUpdate> RecordAmbiguousAsync(
        PublicChatClaimedMessage message,
        PublicChatFailureDiagnostic.Send diagnostic,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    )
    {
        return ExecuteStateTransitionAsync(
            (db, ct) =>
                db.PublicChatOutboxMessages
                    .Where(row =>
                        row.Id == message.Id
                        && row.Status == PublicChatOutboxStatus.Sending
                        && row.ClaimToken == message.ClaimToken.Value
                    )
                    .ExecuteUpdateAsync(
                        update =>
                            update
                                .SetProperty(
                                    row => row.Status,
                                    PublicChatOutboxStatus.Ambiguous
                                )
                                .SetProperty(row => row.Message, (string?)null)
                                .SetProperty(row => row.ClaimToken, (Guid?)null)
                                .SetProperty(row => row.ClaimSlot, (int?)null)
                                .SetProperty(
                                    row => row.ClaimExpiresAtUtc,
                                    (DateTime?)null
                                )
                                .SetProperty(
                                    row => row.CompletedAtUtc,
                                    recordedAt.UtcDateTime
                                )
                                .SetProperty(
                                    row => row.FailurePhase,
                                    PublicChatOutboxFailurePhase.Send
                                )
                                .SetProperty(
                                    row => row.FailureType,
                                    diagnostic.FailureType.Value
                                )
                                .SetProperty(
                                    row => row.HttpStatusCode,
                                    HttpStatusCode(diagnostic.HttpStatus)
                                ),
                        ct
                    ),
            cancellationToken
        );
    }

    private ValueTask<PublicChatClaimUpdate> RecordUnexpectedAsync(
        PublicChatClaimedMessage message,
        PublicChatFailureDiagnostic.Preparation diagnostic,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    )
    {
        return ExecuteStateTransitionAsync(
            (db, ct) =>
                db.PublicChatOutboxMessages
                    .Where(row =>
                        row.Id == message.Id
                        && row.Status == PublicChatOutboxStatus.Claimed
                        && row.ClaimToken == message.ClaimToken.Value
                    )
                    .ExecuteUpdateAsync(
                        update =>
                            update
                                .SetProperty(
                                    row => row.Status,
                                    PublicChatOutboxStatus.Unexpected
                                )
                                .SetProperty(row => row.Message, (string?)null)
                                .SetProperty(row => row.ClaimToken, (Guid?)null)
                                .SetProperty(row => row.ClaimSlot, (int?)null)
                                .SetProperty(
                                    row => row.ClaimExpiresAtUtc,
                                    (DateTime?)null
                                )
                                .SetProperty(
                                    row => row.CompletedAtUtc,
                                    recordedAt.UtcDateTime
                                )
                                .SetProperty(
                                    row => row.FailurePhase,
                                    PublicChatOutboxFailurePhase.Preparation
                                )
                                .SetProperty(
                                    row => row.FailureType,
                                    diagnostic.FailureType.Value
                                )
                                .SetProperty(
                                    row => row.HttpStatusCode,
                                    HttpStatusCode(diagnostic.HttpStatus)
                                ),
                        ct
                    ),
            cancellationToken
        );
    }

    private async ValueTask<PublicChatClaimUpdate> ExecuteStateTransitionAsync(
        Func<BlokeBotDbContext, CancellationToken, Task<int>> transition,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return Changed(await transition(db, cancellationToken));
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

    private static PublicChatClaimedMessage MapClaimed(PublicChatOutboxMessage row)
    {
        if (row.Message is null || row.ClaimToken is null || row.ClaimExpiresAtUtc is null)
        {
            throw new UnreachableException(
                "A claimed public chat outbox row has an invalid persistence shape."
            );
        }

        return new PublicChatClaimedMessage
        {
            Id = row.Id,
            Channel = row.Channel,
            Message = row.Message,
            EnqueuedAt = ToDateTimeOffset(row.CreatedAtUtc),
            Attempt = row.AttemptCount + 1,
            ClaimToken = new PublicChatClaimToken(row.ClaimToken.Value),
            ClaimExpiresAt = ToDateTimeOffset(row.ClaimExpiresAtUtc.Value),
            DeduplicationKey = new PublicChatDeduplicationKey(row.DeduplicationKey),
        };
    }

    private static DateTime EligibleAt(
        PublicChatOutboxMessage row,
        DateTime? previousAttemptAt,
        IReadOnlyDictionary<string, DateTime> deliveredAtByKey,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown
    )
    {
        var eligibleAt = row.NextAttemptAtUtc;
        if (previousAttemptAt is { } attemptAt)
            eligibleAt = Max(eligibleAt, attemptAt + sendInterval);
        if (deliveredAtByKey.TryGetValue(row.DeduplicationKey, out var deliveredAt))
            eligibleAt = Max(eligibleAt, deliveredAt + duplicateCooldown);

        return eligibleAt;
    }

    private static int? HttpStatusCode(PublicChatHttpStatus status) =>
        status.Match<int?>(known => known.Value, () => null);

    private static bool IsSqliteContention(Exception exception) =>
        exception switch
        {
            SqliteException { SqliteErrorCode: 5 or 6 } => true,
            DbUpdateException { InnerException: { } inner } => IsSqliteContention(inner),
            _ => false,
        };

    private static bool IsClaimSlotContention(Exception exception) =>
        exception switch
        {
            SqliteException
            {
                SqliteErrorCode: 19,
                SqliteExtendedErrorCode: 2067,
            } => true,
            DbUpdateException { InnerException: { } inner } =>
                IsClaimSlotContention(inner),
            _ => false,
        };

    private static PublicChatClaimUpdate Changed(int rowCount) =>
        rowCount switch
        {
            0 => new PublicChatClaimUpdate.OwnershipLost(),
            1 => new PublicChatClaimUpdate.Applied(),
            _ => throw new UnreachableException(
                $"A public chat claim transition changed {rowCount} rows."
            ),
        };

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static DateTime Max(DateTime left, DateTime right) =>
        left >= right ? left : right;

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record ClaimCandidate(
        PublicChatOutboxMessage Row,
        DateTime EligibleAtUtc
    );
}
