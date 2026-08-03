using System.Diagnostics;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PublicChat;

internal sealed partial class EfPublicChatOutbox
{
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

    public async ValueTask<PublicChatClaimUpdate> BeginSendAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset sendStartedAt,
        DateTimeOffset claimExpiresAt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            var expired = await ExpireOwnedClaimAsync(
                db,
                message,
                sendStartedAt.UtcDateTime,
                cancellationToken
            );
            if (expired == 1)
            {
                _ = await RecordAutomaticRaidTerminalAsync(
                    db,
                    message,
                    AutomaticRaidShoutoutResultCode.NotReady,
                    sendStartedAt,
                    cancellationToken
                );
                _ = await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new PublicChatClaimUpdate.Expired();
            }

            var changed = await db
                .PublicChatOutboxMessages.Where(row =>
                    row.Id == message.Id
                    && row.Status == PublicChatOutboxStatus.Claimed
                    && row.ClaimToken == message.ClaimToken.Value
                    && row.ClaimExpiresAtUtc > sendStartedAt.UtcDateTime
                    && row.ExpiresAtUtc > sendStartedAt.UtcDateTime
                )
                .ExecuteUpdateAsync(
                    update =>
                        update
                            .SetProperty(row => row.Status, PublicChatOutboxStatus.Sending)
                            .SetProperty(row => row.SendStartedAtUtc, sendStartedAt.UtcDateTime)
                            .SetProperty(row => row.ClaimExpiresAtUtc, claimExpiresAt.UtcDateTime)
                            .SetProperty(row => row.AttemptCount, row => row.AttemptCount + 1)
                            .SetProperty(
                                row => row.FailurePhase,
                                (PublicChatOutboxFailurePhase?)null
                            )
                            .SetProperty(row => row.FailureType, (string?)null)
                            .SetProperty(row => row.HttpStatusCode, (int?)null)
                            .SetProperty(row => row.RejectionCode, (string?)null),
                    cancellationToken
                );
            if (changed == 0)
            {
                return new PublicChatClaimUpdate.OwnershipLost();
            }

            _ = db.PublicChatSendReceipts.Add(
                new PublicChatSendReceipt
                {
                    OutboxMessageId = message.Id,
                    AttemptedAtUtc = sendStartedAt.UtcDateTime,
                }
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PublicChatClaimUpdate.Applied();
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

    public async ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (
                await ExpireOwnedClaimAsync(db, message, releasedAt.UtcDateTime, cancellationToken)
                == 1
            )
            {
                _ = await RecordAutomaticRaidTerminalAsync(
                    db,
                    message,
                    AutomaticRaidShoutoutResultCode.NotReady,
                    releasedAt,
                    cancellationToken
                );
                _ = await db.SaveChangesAsync(cancellationToken);
                return new PublicChatClaimUpdate.Expired();
            }

            var initialClaimReleased = await db
                .PublicChatOutboxMessages.Where(row =>
                    row.Id == message.Id
                    && row.Status == PublicChatOutboxStatus.Claimed
                    && row.ClaimToken == message.ClaimToken.Value
                    && row.SafePreSendFailureCount == 0
                    && row.ExpiresAtUtc > releasedAt.UtcDateTime
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
            if (initialClaimReleased == 1)
            {
                return new PublicChatClaimUpdate.Applied();
            }

            var retryClaimReleased = await db
                .PublicChatOutboxMessages.Where(row =>
                    row.Id == message.Id
                    && row.Status == PublicChatOutboxStatus.Claimed
                    && row.ClaimToken == message.ClaimToken.Value
                    && row.SafePreSendFailureCount > 0
                    && row.ExpiresAtUtc > releasedAt.UtcDateTime
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
            return Changed(retryClaimReleased);
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

    public async ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var nowUtc = now.UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                row.Status == PublicChatOutboxStatus.Sending
                || (
                    row.ExpiresAtUtc > nowUtc
                    && (
                        row.Status == PublicChatOutboxStatus.Pending
                        || row.Status == PublicChatOutboxStatus.Claimed
                        || row.Status == PublicChatOutboxStatus.SafePreSendTransient
                    )
                )
            )
            .OrderBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Channel, row.CreatedAtUtc })
            .ToArrayAsync(cancellationToken);
        return rows.Select(row => new PublicChatPendingMessage(
                row.Channel,
                ToDateTimeOffset(row.CreatedAtUtc)
            ))
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
        await ExpireUnsentBatchAsync(db, nowUtc, cancellationToken);
        await RecoverExpiredAsync(db, nowUtc, cancellationToken);
        await ExhaustConfiguredSafePreSendRetriesAsync(db, nowUtc, cancellationToken);
        await PurgeTerminalBatchAsync(db, nowUtc, cancellationToken);
        await PurgeSendReceiptBatchAsync(
            db,
            nowUtc,
            Max(sendInterval, duplicateCooldown),
            cancellationToken
        );
        var nextTerminalPurgeAt = await NextTerminalPurgeAtAsync(db, now, cancellationToken);
        var nextSendReceiptPurgeAt = await NextSendReceiptPurgeAtAsync(
            db,
            now,
            Max(sendInterval, duplicateCooldown),
            cancellationToken
        );
        var nextExpiryAt = await NextUnsentExpiryAtAsync(db, cancellationToken);
        var nextMaintenanceAt = nextTerminalPurgeAt switch
        {
            { } terminalPurgeAt when nextSendReceiptPurgeAt is { } receiptPurgeAt => Min(
                terminalPurgeAt,
                receiptPurgeAt
            ),
            { } terminalPurgeAt => terminalPurgeAt,
            null => nextSendReceiptPurgeAt,
        };
        if (nextExpiryAt is { } expiryAt)
        {
            nextMaintenanceAt = nextMaintenanceAt is { } maintenanceAt
                ? Min(maintenanceAt, expiryAt)
                : expiryAt;
        }

        var activeClaim = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                (
                    row.Status == PublicChatOutboxStatus.Claimed
                    || row.Status == PublicChatOutboxStatus.Sending
                )
                && row.ClaimExpiresAtUtc > nowUtc
            )
            .OrderBy(row => row.ClaimExpiresAtUtc)
            .Select(row => row.ClaimExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeClaim is { } activeClaimExpiry)
        {
            var claimAvailability = Min(
                ToDateTimeOffset(activeClaimExpiry),
                now + _claimAvailabilityPoll
            );
            return new PublicChatClaimOutcome.AwaitingAvailability(
                nextMaintenanceAt is { } purgeAt
                    ? Min(claimAvailability, purgeAt)
                    : claimAvailability
            );
        }

        var claimable = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                (
                    row.Status == PublicChatOutboxStatus.Pending
                    || (
                        row.Status == PublicChatOutboxStatus.SafePreSendTransient
                        && row.SafePreSendFailureCount < _safePreSendRetryPolicy.AttemptLimit
                    )
                )
                && row.ExpiresAtUtc > nowUtc
            )
            .ToArrayAsync(cancellationToken);
        if (claimable.Length == 0)
        {
            return nextMaintenanceAt is { } purgeAt
                ? new PublicChatClaimOutcome.AwaitingAvailability(purgeAt)
                : new PublicChatClaimOutcome.Empty();
        }

        var previousAttemptAt = await db
            .PublicChatSendReceipts.AsNoTracking()
            .Where(receipt => receipt.CompletedAtUtc != null)
            .OrderByDescending(receipt => receipt.CompletedAtUtc)
            .Select(receipt => receipt.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var duplicateCutoffUtc = SubtractOrMinimum(nowUtc, duplicateCooldown);
        var previousDeliveries = await db
            .PublicChatSendReceipts.AsNoTracking()
            .Where(receipt =>
                receipt.DeliveredDeduplicationKey != null
                && receipt.DeliveredAtUtc > duplicateCutoffUtc
            )
            .Select(receipt => new
            {
                DeduplicationKey = receipt.DeliveredDeduplicationKey!,
                CompletedAtUtc = receipt.DeliveredAtUtc!.Value,
            })
            .ToArrayAsync(cancellationToken);
        var deliveredAtByKey = previousDeliveries
            .GroupBy(row => row.DeduplicationKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Max(row => row.CompletedAtUtc),
                StringComparer.Ordinal
            );
        var candidate = claimable
            .Select(row => new ClaimCandidate(
                row,
                EligibleAt(
                    row,
                    previousAttemptAt,
                    deliveredAtByKey,
                    sendInterval,
                    duplicateCooldown
                )
            ))
            .OrderBy(value => value.EligibleAtUtc)
            .ThenBy(value => value.Row.CreatedAtUtc)
            .ThenBy(value => value.Row.Id)
            .First();
        if (candidate.EligibleAtUtc > nowUtc)
        {
            var candidateAvailableAt = ToDateTimeOffset(candidate.EligibleAtUtc);
            return new PublicChatClaimOutcome.AwaitingAvailability(
                nextMaintenanceAt is { } purgeAt
                    ? Min(candidateAvailableAt, purgeAt)
                    : candidateAvailableAt
            );
        }

        var claimToken = Guid.NewGuid();
        var changed = await db
            .PublicChatOutboxMessages.Where(row =>
                row.Id == candidate.Row.Id
                && row.ExpiresAtUtc > nowUtc
                && (
                    row.Status == PublicChatOutboxStatus.Pending
                    || (
                        row.Status == PublicChatOutboxStatus.SafePreSendTransient
                        && row.SafePreSendFailureCount < _safePreSendRetryPolicy.AttemptLimit
                    )
                )
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(row => row.Status, PublicChatOutboxStatus.Claimed)
                        .SetProperty(row => row.ClaimToken, claimToken)
                        .SetProperty(row => row.ClaimSlot, 1)
                        .SetProperty(row => row.ClaimExpiresAtUtc, claimExpiresAt.UtcDateTime),
                cancellationToken
            );
        if (changed == 0)
        {
            return new PublicChatClaimOutcome.Contended();
        }

        var claimed =
            await db
                .PublicChatOutboxMessages.AsNoTracking()
                .SingleOrDefaultAsync(
                    row => row.Id == candidate.Row.Id && row.ClaimToken == claimToken,
                    cancellationToken
                )
            ?? throw new UnreachableException(
                "A successfully claimed public chat outbox row disappeared."
            );
        return new PublicChatClaimOutcome.Claimed(MapClaimed(claimed));
    }

    private static PublicChatClaimedMessage MapClaimed(PublicChatOutboxMessage row) =>
        row.Message is null
        || row.DeduplicationKey is null
        || row.NextAttemptAtUtc is null
        || row.ClaimToken is null
        || row.ClaimExpiresAtUtc is null
            ? throw new UnreachableException(
                "A claimed public chat outbox row has an invalid persistence shape."
            )
            : new PublicChatClaimedMessage
            {
                Id = row.Id,
                Channel = row.Channel,
                Message = row.Message,
                EnqueuedAt = ToDateTimeOffset(row.CreatedAtUtc),
                ExpiresAt = ToDateTimeOffset(row.ExpiresAtUtc),
                Attempt = row.AttemptCount + 1,
                ClaimToken = new PublicChatClaimToken(row.ClaimToken.Value),
                ClaimExpiresAt = ToDateTimeOffset(row.ClaimExpiresAtUtc.Value),
                DeduplicationKey = new PublicChatDeduplicationKey(row.DeduplicationKey),
            };

    private static DateTime EligibleAt(
        PublicChatOutboxMessage row,
        DateTime? previousAttemptAt,
        IReadOnlyDictionary<string, DateTime> deliveredAtByKey,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown
    )
    {
        var eligibleAt =
            row.NextAttemptAtUtc
            ?? throw new UnreachableException(
                "A claimable public chat outbox row has no next-attempt time."
            );
        if (previousAttemptAt is { } attemptAt)
        {
            eligibleAt = Max(eligibleAt, attemptAt + sendInterval);
        }

        var deduplicationKey =
            row.DeduplicationKey
            ?? throw new UnreachableException(
                "A claimable public chat outbox row has no deduplication key."
            );
        if (deliveredAtByKey.TryGetValue(deduplicationKey, out var deliveredAt))
        {
            eligibleAt = Max(eligibleAt, deliveredAt + duplicateCooldown);
        }

        return eligibleAt;
    }

    private sealed record ClaimCandidate(PublicChatOutboxMessage Row, DateTime EligibleAtUtc);
}
