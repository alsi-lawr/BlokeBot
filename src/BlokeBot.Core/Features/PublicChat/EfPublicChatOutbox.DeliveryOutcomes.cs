using System.Diagnostics;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PublicChat;

internal sealed partial class EfPublicChatOutbox
{
    public ValueTask<PublicChatClaimUpdate> RecordDeliveryOutcomeAsync(
        PublicChatClaimedMessage message,
        PublicChatDeliveryOutcome outcome,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Match(
            sent => RecordSentAsync(message, sent.TwitchMessageId, recordedAt, cancellationToken),
            _ =>
                RecordMissingIdentityAsync(
                    message,
                    PublicChatOutboxStatus.MissingChannel,
                    recordedAt,
                    cancellationToken
                ),
            _ =>
                RecordMissingIdentityAsync(
                    message,
                    PublicChatOutboxStatus.MissingBot,
                    recordedAt,
                    cancellationToken
                ),
            unavailable =>
                RecordUnexpectedAsync(
                    message,
                    new PublicChatFailureDiagnostic.Preparation
                    {
                        FailureType = new PublicChatFailureType(unavailable.Reason.ToString()),
                        HttpStatus = new PublicChatHttpStatus.Unavailable(),
                    },
                    AutomaticRaidShoutoutResultCode.AuthorityRequired,
                    recordedAt,
                    cancellationToken
                ),
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

    private async ValueTask<PublicChatClaimUpdate> RecordSentAsync(
        PublicChatClaimedMessage message,
        string twitchMessageId,
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
                .SingleOrDefaultAsync(cancellationToken);
            if (deleted is null)
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
                            .SetProperty(receipt => receipt.DeliveredAtUtc, completedAt.UtcDateTime)
                            .SetProperty(receipt => receipt.TwitchMessageId, twitchMessageId),
                    cancellationToken
                );
            if (receiptUpdated != 1)
            {
                throw new UnreachableException(
                    $"A confirmed public chat send updated {receiptUpdated} send receipts."
                );
            }

            await db
                .PublicChatPinOperations.Where(operation =>
                    operation.OutboxMessageId == message.Id
                    && operation.Status == PublicChatPinOperationStatus.AwaitingDelivery
                )
                .ExecuteUpdateAsync(
                    update =>
                        update
                            .SetProperty(
                                operation => operation.Status,
                                PublicChatPinOperationStatus.Ready
                            )
                            .SetProperty(operation => operation.OutboxMessageId, (long?)null)
                            .SetProperty(operation => operation.TwitchMessageId, twitchMessageId),
                    cancellationToken
                );

            db.PublicChatOutboxMessages.Remove(deleted);
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
                PublicChatClaimUpdate expired = Changed(
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
                if (expired is PublicChatClaimUpdate.Expired)
                {
                    _ = await RecordAutomaticRaidTerminalAsync(
                        db,
                        message,
                        AutomaticRaidShoutoutResultCode.NotReady,
                        recordedAt,
                        cancellationToken
                    );
                    await db.SaveChangesAsync(cancellationToken);
                }
                return expired;
            }

            var persistedFailureCount = persisted.SafePreSendFailureCount;

            var decision = PublicChatSafePreSendRetrySchedule.Create(
                _safePreSendRetryPolicy,
                new PublicChatSafePreSendFailureCount(persistedFailureCount),
                recordedAt
            );
            var update = decision switch
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
            if (
                decision is PublicChatSafePreSendRetryDecision.Exhausted
                && update is PublicChatClaimUpdate.Applied
            )
            {
                _ = await RecordAutomaticRaidTerminalAsync(
                    db,
                    message,
                    AutomaticRaidShoutoutResultCode.Unexpected,
                    recordedAt,
                    cancellationToken
                );
                await db.SaveChangesAsync(cancellationToken);
            }
            return update;
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }

    private async ValueTask<PublicChatClaimUpdate> RecordMissingIdentityAsync(
        PublicChatClaimedMessage message,
        PublicChatOutboxStatus status,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    )
    {
        if (
            status
            is not PublicChatOutboxStatus.MissingChannel
                and not PublicChatOutboxStatus.MissingBot
        )
        {
            throw new UnreachableException(
                "A public chat identity outcome mapped to a non-identity status."
            );
        }

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
            _ = await RecordAutomaticRaidTerminalAsync(
                expiryDb,
                message,
                AutomaticRaidShoutoutResultCode.NotReady,
                recordedAt,
                cancellationToken
            );
            await expiryDb.SaveChangesAsync(cancellationToken);
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
                                .SetProperty(row => row.Status, status)
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
                                .SetProperty(row => row.FailureType, (string?)null)
                                .SetProperty(row => row.HttpStatusCode, (int?)null)
                                .SetProperty(row => row.RejectionCode, (string?)null),
                        ct
                    ),
            message,
            AutomaticRaidShoutoutResultCode.AuthorityRequired,
            recordedAt,
            cancellationToken
        );
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
            AutomaticRaidShoutoutResultCode.Rejected,
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
            AutomaticRaidShoutoutResultCode.Ambiguous,
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
        return await RecordUnexpectedAsync(
            message,
            diagnostic,
            AutomaticRaidShoutoutResultCode.Unexpected,
            recordedAt,
            cancellationToken
        );
    }

    private async ValueTask<PublicChatClaimUpdate> RecordUnexpectedAsync(
        PublicChatClaimedMessage message,
        PublicChatFailureDiagnostic.Preparation diagnostic,
        AutomaticRaidShoutoutResultCode automaticRaidResult,
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
            _ = await RecordAutomaticRaidTerminalAsync(
                expiryDb,
                message,
                AutomaticRaidShoutoutResultCode.NotReady,
                recordedAt,
                cancellationToken
            );
            await expiryDb.SaveChangesAsync(cancellationToken);
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
            message,
            automaticRaidResult,
            recordedAt,
            cancellationToken
        );
    }

    private async ValueTask<PublicChatClaimUpdate> ExecuteSendTerminalTransitionAsync(
        PublicChatClaimedMessage message,
        Func<BlokeBotDbContext, CancellationToken, Task<int>> transition,
        DateTimeOffset completedAt,
        AutomaticRaidShoutoutResultCode automaticRaidResult,
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

            var alertCreated = await RecordAutomaticRaidTerminalAsync(
                db,
                message,
                automaticRaidResult,
                completedAt,
                cancellationToken
            );
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (alertCreated && events is not null)
            {
                await events.PublishAsync(AppEventKind.AlertsChanged, cancellationToken);
            }
            return new PublicChatClaimUpdate.Applied();
        }
        catch (Exception exception) when (IsSqliteContention(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PublicChatClaimUpdate.Contended();
        }
    }
}
