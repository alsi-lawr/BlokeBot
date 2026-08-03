using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PublicChat;

internal sealed partial class EfPublicChatOutbox
{
    private static async Task ExpireUnsentBatchAsync(
        BlokeBotDbContext db,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        var expiring = await db
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
            .Select(row => new { row.Id, row.DeduplicationKey })
            .Take(_cleanupBatchSize)
            .ToArrayAsync(cancellationToken);
        if (expiring.Length == 0)
        {
            return;
        }
        var ids = expiring.Select(row => row.Id).ToArray();

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
        foreach (var row in expiring)
        {
            if (row.DeduplicationKey is null)
            {
                continue;
            }
            _ = await RecordAutomaticRaidTerminalAsync(
                db,
                row.DeduplicationKey,
                row.Id,
                AutomaticRaidShoutoutResultCode.NotReady,
                ToDateTimeOffset(nowUtc),
                cancellationToken
            );
        }
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private static Task<int> ExpireOwnedClaimAsync(
        BlokeBotDbContext db,
        PublicChatClaimedMessage message,
        DateTime nowUtc,
        CancellationToken cancellationToken
    ) =>
        db
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
        var expiredSending = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                row.Status == PublicChatOutboxStatus.Sending && row.ClaimExpiresAtUtc <= nowUtc
            )
            .Select(row => new { row.Id, row.DeduplicationKey })
            .ToArrayAsync(cancellationToken);
        var expiredSendingIds = expiredSending.Select(row => row.Id).ToArray();
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

        _ = await db
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
        foreach (var row in expiredSending)
        {
            if (row.DeduplicationKey is null)
            {
                continue;
            }
            _ = await RecordAutomaticRaidTerminalAsync(
                db,
                row.DeduplicationKey,
                row.Id,
                AutomaticRaidShoutoutResultCode.Ambiguous,
                ToDateTimeOffset(nowUtc),
                cancellationToken
            );
        }
        _ = await db
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
        _ = await db
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
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ExhaustConfiguredSafePreSendRetriesAsync(
        BlokeBotDbContext db,
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        var exhausted = await db
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row =>
                row.Status == PublicChatOutboxStatus.SafePreSendTransient
                && row.SafePreSendFailureCount >= _safePreSendRetryPolicy.AttemptLimit
                && row.ExpiresAtUtc > nowUtc
            )
            .Select(row => new
            {
                row.Id,
                row.DeduplicationKey,
                row.HttpStatusCode,
            })
            .ToArrayAsync(cancellationToken);
        _ = await db
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
        foreach (var row in exhausted)
        {
            if (row.DeduplicationKey is null)
            {
                continue;
            }
            _ = await RecordAutomaticRaidTerminalAsync(
                db,
                row.DeduplicationKey,
                row.Id,
                SafePreSendExhaustionResult(row.HttpStatusCode),
                ToDateTimeOffset(nowUtc),
                cancellationToken
            );
        }
        _ = await db.SaveChangesAsync(cancellationToken);
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

    private static IQueryable<PublicChatOutboxMessage> TerminalRows(BlokeBotDbContext db) =>
        db.PublicChatOutboxMessages.Where(row =>
            row.Status == PublicChatOutboxStatus.SafePreSendExhausted
            || row.Status == PublicChatOutboxStatus.MissingChannel
            || row.Status == PublicChatOutboxStatus.MissingBot
            || row.Status == PublicChatOutboxStatus.Rejected
            || row.Status == PublicChatOutboxStatus.Ambiguous
            || row.Status == PublicChatOutboxStatus.Unexpected
            || row.Status == PublicChatOutboxStatus.Expired
        );
}
