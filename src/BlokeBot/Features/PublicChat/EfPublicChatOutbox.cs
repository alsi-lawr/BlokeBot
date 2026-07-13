using System.Diagnostics;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.PublicChat;

internal sealed class EfPublicChatOutbox(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PublicChatRetryPolicy retryPolicy,
    PublicChatDeliveryLifetimePolicy lifetimePolicy,
    PublicChatTerminalRetentionPolicy retentionPolicy
) : IPublicChatOutbox
{
    private static readonly TimeSpan _claimAvailabilityPoll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _maximumMaintenanceWake = TimeSpan.FromDays(30);
    private const int _cleanupBatchSize = 100;
    private readonly PublicChatRetryPolicy _safePreSendRetryPolicy =
        retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
    private readonly PublicChatDeliveryLifetimePolicy _deliveryLifetimePolicy =
        lifetimePolicy ?? throw new ArgumentNullException(nameof(lifetimePolicy));
    private readonly PublicChatTerminalRetentionPolicy _terminalRetentionPolicy =
        retentionPolicy ?? throw new ArgumentNullException(nameof(retentionPolicy));

    public async ValueTask<PublicChatEnqueueOutcome> EnqueueAsync(
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
            throw new ArgumentException(
                "At least one non-blank message is required.",
                nameof(batch)
            );
        }

        var createdAtUtc = batch.EnqueuedAt.UtcDateTime;
        var configuredExpiry = batch.EnqueuedAt.Add(_deliveryLifetimePolicy.MaximumAge);
        var expiresAtUtc = batch.Deadline switch
        {
            PublicChatDeliveryDeadline.ConfiguredMaximum => configuredExpiry.UtcDateTime,
            PublicChatDeliveryDeadline.ProducerAbsolute producer => Min(
                configuredExpiry,
                producer.ExpiresAt
            ).UtcDateTime,
            _ => throw new UnreachableException("Unknown public-chat delivery deadline."),
        };
        var rows = batch
            .Items.Select(item => new PublicChatOutboxMessage
            {
                Channel = batch.Channel,
                Message = item.Message,
                DeduplicationKey = item.DeduplicationKey.Value,
                CreatedAtUtc = createdAtUtc,
                ExpiresAtUtc = expiresAtUtc,
                NextAttemptAtUtc = createdAtUtc,
            })
            .ToArray();

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.PublicChatOutboxMessages.AddRange(rows);
            await db.SaveChangesAsync(cancellationToken);
            return new PublicChatEnqueueOutcome.Accepted(
                new PublicChatOutboxReceipt([.. rows.Select(row => row.Id)])
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatEnqueueOutcome.SafePreEnqueueTransient(exception);
        }
        catch (DbUpdateException exception)
        {
            return new PublicChatEnqueueOutcome.Ambiguous(exception);
        }
        catch (Exception exception)
        {
            return new PublicChatEnqueueOutcome.Unexpected(exception);
        }
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

            db.PublicChatSendReceipts.Add(
                new PublicChatSendReceipt
                {
                    OutboxMessageId = message.Id,
                    AttemptedAtUtc = sendStartedAt.UtcDateTime,
                }
            );
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PublicChatClaimUpdate.Applied();
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

    public ValueTask<PublicChatClaimUpdate> RecordDeliveryOutcomeAsync(
        PublicChatClaimedMessage message,
        PublicChatDeliveryOutcome outcome,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Match(
            _ => RecordSentAsync(message, recordedAt, cancellationToken),
            transient =>
                RecordSafePreSendTransientAsync(
                    message,
                    transient.Diagnostic,
                    recordedAt,
                    cancellationToken
                ),
            rejection =>
                RecordRejectionAsync(message, rejection.Reason, recordedAt, cancellationToken),
            ambiguous =>
                RecordAmbiguousAsync(message, ambiguous.Diagnostic, recordedAt, cancellationToken),
            unexpected =>
                RecordUnexpectedAsync(message, unexpected.Diagnostic, recordedAt, cancellationToken)
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
        return RecordAmbiguousAsync(message, diagnostic, interruptedAt, cancellationToken);
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

    private static async Task ExpireUnsentBatchAsync(
        BlokeBotDbContext db,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        var ids = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                row.ExpiresAtUtc <= nowUtc
                && (
                    row.Status == PublicChatOutboxStatus.Pending
                    || row.Status == PublicChatOutboxStatus.Claimed
                    || row.Status == PublicChatOutboxStatus.SafePreSendTransient
                )
            )
            .OrderBy(row => row.ExpiresAtUtc)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .Take(_cleanupBatchSize)
            .ToArrayAsync(cancellationToken);
        if (ids.Length == 0)
        {
            return;
        }

        _ = await db
            .PublicChatOutboxMessages.Where(row =>
                ids.Contains(row.Id)
                && row.ExpiresAtUtc <= nowUtc
                && (
                    row.Status == PublicChatOutboxStatus.Pending
                    || row.Status == PublicChatOutboxStatus.Claimed
                    || row.Status == PublicChatOutboxStatus.SafePreSendTransient
                )
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(row => row.Status, PublicChatOutboxStatus.Expired)
                        .SetProperty(row => row.Message, (string?)null)
                        .SetProperty(row => row.DeduplicationKey, (string?)null)
                        .SetProperty(row => row.NextAttemptAtUtc, (DateTime?)null)
                        .SetProperty(row => row.ClaimToken, (Guid?)null)
                        .SetProperty(row => row.ClaimSlot, (int?)null)
                        .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null)
                        .SetProperty(row => row.SendStartedAtUtc, (DateTime?)null)
                        .SetProperty(row => row.CompletedAtUtc, nowUtc)
                        .SetProperty(row => row.FailurePhase, (PublicChatOutboxFailurePhase?)null)
                        .SetProperty(row => row.FailureType, (string?)null)
                        .SetProperty(row => row.HttpStatusCode, (int?)null)
                        .SetProperty(row => row.RejectionCode, (string?)null),
                cancellationToken
            );
    }

    private static Task<int> ExpireOwnedClaimAsync(
        BlokeBotDbContext db,
        PublicChatClaimedMessage message,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        return db
            .PublicChatOutboxMessages.Where(row =>
                row.Id == message.Id
                && row.Status == PublicChatOutboxStatus.Claimed
                && row.ClaimToken == message.ClaimToken.Value
                && row.ExpiresAtUtc <= nowUtc
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(row => row.Status, PublicChatOutboxStatus.Expired)
                        .SetProperty(row => row.Message, (string?)null)
                        .SetProperty(row => row.DeduplicationKey, (string?)null)
                        .SetProperty(row => row.NextAttemptAtUtc, (DateTime?)null)
                        .SetProperty(row => row.ClaimToken, (Guid?)null)
                        .SetProperty(row => row.ClaimSlot, (int?)null)
                        .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null)
                        .SetProperty(row => row.SendStartedAtUtc, (DateTime?)null)
                        .SetProperty(row => row.CompletedAtUtc, nowUtc)
                        .SetProperty(row => row.FailurePhase, (PublicChatOutboxFailurePhase?)null)
                        .SetProperty(row => row.FailureType, (string?)null)
                        .SetProperty(row => row.HttpStatusCode, (int?)null)
                        .SetProperty(row => row.RejectionCode, (string?)null),
                cancellationToken
            );
    }

    private static async Task<DateTimeOffset?> NextUnsentExpiryAtAsync(
        BlokeBotDbContext db,
        CancellationToken cancellationToken
    )
    {
        var expiresAtUtc = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                row.Status == PublicChatOutboxStatus.Pending
                || row.Status == PublicChatOutboxStatus.Claimed
                || row.Status == PublicChatOutboxStatus.SafePreSendTransient
            )
            .OrderBy(row => row.ExpiresAtUtc)
            .Select(row => (DateTime?)row.ExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return expiresAtUtc is { } value ? ToDateTimeOffset(value) : null;
    }

    private static async Task RecoverExpiredAsync(
        BlokeBotDbContext db,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        var expiredSendingIds = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                row.Status == PublicChatOutboxStatus.Sending && row.ClaimExpiresAtUtc <= nowUtc
            )
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);
        if (expiredSendingIds.Length > 0)
        {
            _ = await db
                .PublicChatSendReceipts.Where(receipt =>
                    expiredSendingIds.Contains(receipt.OutboxMessageId)
                    && receipt.CompletedAtUtc == null
                )
                .ExecuteUpdateAsync(
                    update => update.SetProperty(receipt => receipt.CompletedAtUtc, nowUtc),
                    cancellationToken
                );
        }

        await db
            .PublicChatOutboxMessages.Where(row =>
                row.Status == PublicChatOutboxStatus.Sending && row.ClaimExpiresAtUtc <= nowUtc
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(row => row.Status, PublicChatOutboxStatus.Ambiguous)
                        .SetProperty(row => row.Message, (string?)null)
                        .SetProperty(row => row.DeduplicationKey, (string?)null)
                        .SetProperty(row => row.NextAttemptAtUtc, (DateTime?)null)
                        .SetProperty(row => row.ClaimToken, (Guid?)null)
                        .SetProperty(row => row.ClaimSlot, (int?)null)
                        .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null)
                        .SetProperty(row => row.CompletedAtUtc, nowUtc)
                        .SetProperty(row => row.FailurePhase, PublicChatOutboxFailurePhase.Send)
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
                        .SetProperty(row => row.Status, PublicChatOutboxStatus.SafePreSendTransient)
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
    )
    {
        return db
            .PublicChatOutboxMessages.Where(row =>
                row.Status == PublicChatOutboxStatus.SafePreSendTransient
                && row.SafePreSendFailureCount >= _safePreSendRetryPolicy.AttemptLimit
                && row.ExpiresAtUtc > nowUtc
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(row => row.Status, PublicChatOutboxStatus.SafePreSendExhausted)
                        .SetProperty(row => row.Message, (string?)null)
                        .SetProperty(row => row.DeduplicationKey, (string?)null)
                        .SetProperty(row => row.NextAttemptAtUtc, (DateTime?)null)
                        .SetProperty(row => row.CompletedAtUtc, nowUtc),
                cancellationToken
            );
    }

    private async Task PurgeTerminalBatchAsync(
        BlokeBotDbContext db,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        var cutoffUtc = SubtractOrMinimum(nowUtc, _terminalRetentionPolicy.Duration);
        var ids = await TerminalRows(db)
            .Where(row => row.CompletedAtUtc <= cutoffUtc)
            .OrderBy(row => row.CompletedAtUtc)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .Take(_cleanupBatchSize)
            .ToArrayAsync(cancellationToken);
        if (ids.Length == 0)
        {
            return;
        }

        _ = await TerminalRows(db)
            .Where(row => ids.Contains(row.Id) && row.CompletedAtUtc <= cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task PurgeSendReceiptBatchAsync(
        BlokeBotDbContext db,
        DateTime nowUtc,
        TimeSpan historyWindow,
        CancellationToken cancellationToken
    )
    {
        var cutoffUtc = SubtractOrMinimum(nowUtc, historyWindow);
        var ids = await db
            .PublicChatSendReceipts.AsNoTracking()
            .Where(receipt =>
                (receipt.CompletedAtUtc ?? receipt.AttemptedAtUtc) <= cutoffUtc
                && (receipt.DeliveredAtUtc == null || receipt.DeliveredAtUtc <= cutoffUtc)
                && !db.PublicChatOutboxMessages.Any(row =>
                    row.Id == receipt.OutboxMessageId
                    && row.Status == PublicChatOutboxStatus.Sending
                )
            )
            .OrderBy(receipt => receipt.AttemptedAtUtc)
            .ThenBy(receipt => receipt.OutboxMessageId)
            .Select(receipt => receipt.OutboxMessageId)
            .Take(_cleanupBatchSize)
            .ToArrayAsync(cancellationToken);
        if (ids.Length == 0)
        {
            return;
        }

        _ = await db
            .PublicChatSendReceipts.Where(receipt =>
                ids.Contains(receipt.OutboxMessageId)
                && (receipt.CompletedAtUtc ?? receipt.AttemptedAtUtc) <= cutoffUtc
                && (receipt.DeliveredAtUtc == null || receipt.DeliveredAtUtc <= cutoffUtc)
                && !db.PublicChatOutboxMessages.Any(row =>
                    row.Id == receipt.OutboxMessageId
                    && row.Status == PublicChatOutboxStatus.Sending
                )
            )
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<DateTimeOffset?> NextTerminalPurgeAtAsync(
        BlokeBotDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var completedAtUtc = await TerminalRows(db)
            .OrderBy(row => row.CompletedAtUtc)
            .Select(row => row.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (completedAtUtc is not { } completedAt)
        {
            return null;
        }

        var exactPurgeAt = AddOrMaximum(
            ToDateTimeOffset(completedAt),
            _terminalRetentionPolicy.Duration
        );
        return Min(exactPurgeAt, AddOrMaximum(now, _maximumMaintenanceWake));
    }

    private static async Task<DateTimeOffset?> NextSendReceiptPurgeAtAsync(
        BlokeBotDbContext db,
        DateTimeOffset now,
        TimeSpan historyWindow,
        CancellationToken cancellationToken
    )
    {
        var completedAtUtc = await db
            .PublicChatSendReceipts.AsNoTracking()
            .Where(receipt =>
                receipt.CompletedAtUtc != null
                && !db.PublicChatOutboxMessages.Any(row =>
                    row.Id == receipt.OutboxMessageId
                    && row.Status == PublicChatOutboxStatus.Sending
                )
            )
            .OrderBy(receipt => receipt.CompletedAtUtc)
            .Select(receipt => receipt.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (completedAtUtc is not { } completedAt)
        {
            return null;
        }

        var exactPurgeAt = AddOrMaximum(ToDateTimeOffset(completedAt), historyWindow);
        return Min(exactPurgeAt, AddOrMaximum(now, _maximumMaintenanceWake));
    }

    private static IQueryable<PublicChatOutboxMessage> TerminalRows(BlokeBotDbContext db)
    {
        return db.PublicChatOutboxMessages.Where(row =>
            row.Status == PublicChatOutboxStatus.SafePreSendExhausted
            || row.Status == PublicChatOutboxStatus.Rejected
            || row.Status == PublicChatOutboxStatus.Ambiguous
            || row.Status == PublicChatOutboxStatus.Unexpected
            || row.Status == PublicChatOutboxStatus.Expired
        );
    }

    private async ValueTask<PublicChatClaimUpdate> RecordSentAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            var deleted = await db
                .PublicChatOutboxMessages.Where(row =>
                    row.Id == message.Id
                    && row.Status == PublicChatOutboxStatus.Sending
                    && row.ClaimToken == message.ClaimToken.Value
                )
                .ExecuteDeleteAsync(cancellationToken);
            if (deleted == 0)
            {
                return new PublicChatClaimUpdate.OwnershipLost();
            }

            var receiptUpdated = await db
                .PublicChatSendReceipts.Where(receipt => receipt.OutboxMessageId == message.Id)
                .ExecuteUpdateAsync(
                    update =>
                        update
                            .SetProperty(
                                receipt => receipt.DeliveredDeduplicationKey,
                                message.DeduplicationKey.Value
                            )
                            .SetProperty(receipt => receipt.CompletedAtUtc, completedAt.UtcDateTime)
                            .SetProperty(
                                receipt => receipt.DeliveredAtUtc,
                                completedAt.UtcDateTime
                            ),
                    cancellationToken
                );
            if (receiptUpdated != 1)
            {
                throw new UnreachableException(
                    $"A confirmed public chat send updated {receiptUpdated} send receipts."
                );
            }

            await transaction.CommitAsync(cancellationToken);
            return new PublicChatClaimUpdate.Applied();
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

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
            var persisted = await db
                .PublicChatOutboxMessages.AsNoTracking()
                .Where(row =>
                    row.Id == message.Id
                    && row.Status == PublicChatOutboxStatus.Claimed
                    && row.ClaimToken == message.ClaimToken.Value
                )
                .Select(row => new { row.SafePreSendFailureCount, row.ExpiresAtUtc })
                .SingleOrDefaultAsync(cancellationToken);
            if (persisted is null)
            {
                return new PublicChatClaimUpdate.OwnershipLost();
            }

            if (persisted.ExpiresAtUtc <= recordedAt.UtcDateTime)
            {
                return Changed(
                    await ExpireOwnedClaimAsync(
                        db,
                        message,
                        recordedAt.UtcDateTime,
                        cancellationToken
                    )
                ) switch
                {
                    PublicChatClaimUpdate.Applied => new PublicChatClaimUpdate.Expired(),
                    PublicChatClaimUpdate.OwnershipLost =>
                        new PublicChatClaimUpdate.OwnershipLost(),
                    _ => throw new UnreachableException(
                        "An owned public chat expiry returned an invalid transition."
                    ),
                };
            }

            var persistedFailureCount = persisted.SafePreSendFailureCount;

            var decision = PublicChatSafePreSendRetrySchedule.Create(
                _safePreSendRetryPolicy,
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
                            && row.ExpiresAtUtc > recordedAt.UtcDateTime
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
                                        Min(
                                            scheduled.NextAttemptAtUtc,
                                            ToDateTimeOffset(persisted.ExpiresAtUtc)
                                        ).UtcDateTime
                                    )
                                    .SetProperty(row => row.ClaimToken, (Guid?)null)
                                    .SetProperty(row => row.ClaimSlot, (int?)null)
                                    .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null)
                                    .SetProperty(row => row.CompletedAtUtc, (DateTime?)null)
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
                                    .SetProperty(row => row.RejectionCode, (string?)null),
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
                            && row.ExpiresAtUtc > recordedAt.UtcDateTime
                        )
                        .ExecuteUpdateAsync(
                            update =>
                                update
                                    .SetProperty(
                                        row => row.Status,
                                        PublicChatOutboxStatus.SafePreSendExhausted
                                    )
                                    .SetProperty(row => row.Message, (string?)null)
                                    .SetProperty(row => row.DeduplicationKey, (string?)null)
                                    .SetProperty(
                                        row => row.SafePreSendFailureCount,
                                        exhausted.FailureCount.Value
                                    )
                                    .SetProperty(row => row.NextAttemptAtUtc, (DateTime?)null)
                                    .SetProperty(row => row.ClaimToken, (Guid?)null)
                                    .SetProperty(row => row.ClaimSlot, (int?)null)
                                    .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null)
                                    .SetProperty(row => row.SendStartedAtUtc, (DateTime?)null)
                                    .SetProperty(row => row.CompletedAtUtc, recordedAt.UtcDateTime)
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
                                    .SetProperty(row => row.RejectionCode, (string?)null),
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
        return ExecuteSendTerminalTransitionAsync(
            message,
            (db, ct) =>
                db
                    .PublicChatOutboxMessages.Where(row =>
                        row.Id == message.Id
                        && row.Status == PublicChatOutboxStatus.Sending
                        && row.ClaimToken == message.ClaimToken.Value
                    )
                    .ExecuteUpdateAsync(
                        update =>
                            update
                                .SetProperty(row => row.Status, PublicChatOutboxStatus.Rejected)
                                .SetProperty(row => row.Message, (string?)null)
                                .SetProperty(row => row.DeduplicationKey, (string?)null)
                                .SetProperty(row => row.NextAttemptAtUtc, (DateTime?)null)
                                .SetProperty(row => row.ClaimToken, (Guid?)null)
                                .SetProperty(row => row.ClaimSlot, (int?)null)
                                .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null)
                                .SetProperty(row => row.CompletedAtUtc, recordedAt.UtcDateTime)
                                .SetProperty(
                                    row => row.FailurePhase,
                                    PublicChatOutboxFailurePhase.Send
                                )
                                .SetProperty(row => row.RejectionCode, rejectionCode),
                        ct
                    ),
            recordedAt,
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
        return ExecuteSendTerminalTransitionAsync(
            message,
            (db, ct) =>
                db
                    .PublicChatOutboxMessages.Where(row =>
                        row.Id == message.Id
                        && row.Status == PublicChatOutboxStatus.Sending
                        && row.ClaimToken == message.ClaimToken.Value
                    )
                    .ExecuteUpdateAsync(
                        update =>
                            update
                                .SetProperty(row => row.Status, PublicChatOutboxStatus.Ambiguous)
                                .SetProperty(row => row.Message, (string?)null)
                                .SetProperty(row => row.DeduplicationKey, (string?)null)
                                .SetProperty(row => row.NextAttemptAtUtc, (DateTime?)null)
                                .SetProperty(row => row.ClaimToken, (Guid?)null)
                                .SetProperty(row => row.ClaimSlot, (int?)null)
                                .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null)
                                .SetProperty(row => row.CompletedAtUtc, recordedAt.UtcDateTime)
                                .SetProperty(
                                    row => row.FailurePhase,
                                    PublicChatOutboxFailurePhase.Send
                                )
                                .SetProperty(row => row.FailureType, diagnostic.FailureType.Value)
                                .SetProperty(
                                    row => row.HttpStatusCode,
                                    HttpStatusCode(diagnostic.HttpStatus)
                                ),
                        ct
                    ),
            recordedAt,
            cancellationToken
        );
    }

    private async ValueTask<PublicChatClaimUpdate> RecordUnexpectedAsync(
        PublicChatClaimedMessage message,
        PublicChatFailureDiagnostic.Preparation diagnostic,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    )
    {
        await using var expiryDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (
            await ExpireOwnedClaimAsync(
                expiryDb,
                message,
                recordedAt.UtcDateTime,
                cancellationToken
            ) == 1
        )
        {
            return new PublicChatClaimUpdate.Expired();
        }

        return await ExecuteStateTransitionAsync(
            (db, ct) =>
                db
                    .PublicChatOutboxMessages.Where(row =>
                        row.Id == message.Id
                        && row.Status == PublicChatOutboxStatus.Claimed
                        && row.ClaimToken == message.ClaimToken.Value
                        && row.ExpiresAtUtc > recordedAt.UtcDateTime
                    )
                    .ExecuteUpdateAsync(
                        update =>
                            update
                                .SetProperty(row => row.Status, PublicChatOutboxStatus.Unexpected)
                                .SetProperty(row => row.Message, (string?)null)
                                .SetProperty(row => row.DeduplicationKey, (string?)null)
                                .SetProperty(row => row.NextAttemptAtUtc, (DateTime?)null)
                                .SetProperty(row => row.ClaimToken, (Guid?)null)
                                .SetProperty(row => row.ClaimSlot, (int?)null)
                                .SetProperty(row => row.ClaimExpiresAtUtc, (DateTime?)null)
                                .SetProperty(row => row.CompletedAtUtc, recordedAt.UtcDateTime)
                                .SetProperty(
                                    row => row.FailurePhase,
                                    PublicChatOutboxFailurePhase.Preparation
                                )
                                .SetProperty(row => row.FailureType, diagnostic.FailureType.Value)
                                .SetProperty(
                                    row => row.HttpStatusCode,
                                    HttpStatusCode(diagnostic.HttpStatus)
                                ),
                        ct
                    ),
            cancellationToken
        );
    }

    private async ValueTask<PublicChatClaimUpdate> ExecuteSendTerminalTransitionAsync(
        PublicChatClaimedMessage message,
        Func<BlokeBotDbContext, CancellationToken, Task<int>> transition,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            var changed = await transition(db, cancellationToken);
            if (changed == 0)
            {
                return new PublicChatClaimUpdate.OwnershipLost();
            }

            var receiptUpdated = await db
                .PublicChatSendReceipts.Where(receipt =>
                    receipt.OutboxMessageId == message.Id && receipt.CompletedAtUtc == null
                )
                .ExecuteUpdateAsync(
                    update =>
                        update.SetProperty(
                            receipt => receipt.CompletedAtUtc,
                            completedAt.UtcDateTime
                        ),
                    cancellationToken
                );
            if (receiptUpdated != 1)
            {
                throw new UnreachableException(
                    $"A terminal public chat send updated {receiptUpdated} send receipts."
                );
            }

            await transaction.CommitAsync(cancellationToken);
            return new PublicChatClaimUpdate.Applied();
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
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
        if (
            row.Message is null
            || row.DeduplicationKey is null
            || row.NextAttemptAtUtc is null
            || row.ClaimToken is null
            || row.ClaimExpiresAtUtc is null
        )
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
            ExpiresAt = ToDateTimeOffset(row.ExpiresAtUtc),
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

    private static int? HttpStatusCode(PublicChatHttpStatus status)
    {
        return status.Match<int?>(known => known.Value, () => null);
    }

    private static bool IsSqliteContention(Exception exception)
    {
        return exception switch
        {
            SqliteException { SqliteErrorCode: 5 or 6 } => true,
            DbUpdateException { InnerException: { } inner } => IsSqliteContention(inner),
            _ => false,
        };
    }

    private static bool IsClaimSlotContention(Exception exception)
    {
        return exception switch
        {
            SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 } => true,
            DbUpdateException { InnerException: { } inner } => IsClaimSlotContention(inner),
            _ => false,
        };
    }

    private static PublicChatClaimUpdate Changed(int rowCount)
    {
        return rowCount switch
        {
            0 => new PublicChatClaimUpdate.OwnershipLost(),
            1 => new PublicChatClaimUpdate.Applied(),
            _ => throw new UnreachableException(
                $"A public chat claim transition changed {rowCount} rows."
            ),
        };
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left <= right ? left : right;
    }

    private static DateTime Max(DateTime left, DateTime right)
    {
        return left >= right ? left : right;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
    {
        return left >= right ? left : right;
    }

    private static DateTime SubtractOrMinimum(DateTime value, TimeSpan duration)
    {
        return duration.Ticks >= value.Ticks - DateTime.MinValue.Ticks
            ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
            : value - duration;
    }

    private static DateTimeOffset AddOrMaximum(DateTimeOffset value, TimeSpan duration)
    {
        return duration.Ticks >= DateTimeOffset.MaxValue.UtcTicks - value.UtcTicks
            ? DateTimeOffset.MaxValue
            : value.Add(duration);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private sealed record ClaimCandidate(PublicChatOutboxMessage Row, DateTime EligibleAtUtc);
}
