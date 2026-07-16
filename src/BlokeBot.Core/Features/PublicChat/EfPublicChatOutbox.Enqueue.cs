using System.Diagnostics;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PublicChat;

internal sealed partial class EfPublicChatOutbox(
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
}
