using System.Diagnostics;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PublicChat;

internal sealed partial class EfPublicChatOutbox(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PublicChatRetryPolicy retryPolicy,
    PublicChatDeliveryLifetimePolicy lifetimePolicy,
    PublicChatTerminalRetentionPolicy retentionPolicy,
    DurableAlertService? alerts = null,
    AutomaticRaidShoutoutOutcomeAuthority? automaticRaidOutcomes = null
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
    private readonly AutomaticRaidShoutoutOutcomeAuthority _automaticRaidOutcomes =
        automaticRaidOutcomes ?? new AutomaticRaidShoutoutOutcomeAuthority();

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
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            db.PublicChatOutboxMessages.AddRange(rows);
            _ = await db.SaveChangesAsync(cancellationToken);
            foreach (var pair in rows.Zip(batch.Items))
            {
                if (pair.Second.PinIntent is not { } intent)
                {
                    continue;
                }

                var normalizedChannel = Login.Normalize(batch.Channel);
                var hostMatchesChannel = await db.Hosts.AnyAsync(
                    host => host.Id == intent.HostId && host.Login == normalizedChannel,
                    cancellationToken
                );
                var ownerIsActive =
                    hostMatchesChannel
                    && (
                        intent.Feature != "guessing"
                        || await db.Rounds.AnyAsync(
                            round =>
                                round.Id == intent.OwnerId
                                && round.HostId == intent.HostId
                                && round.Status == GuessRoundStatus.Open,
                            cancellationToken
                        )
                    );
                if (!ownerIsActive)
                {
                    continue;
                }

                _ = db.PublicChatPinOperations.Add(
                    new PublicChatPinOperation
                    {
                        Kind = PublicChatPinOperationKind.Pin,
                        Status = PublicChatPinOperationStatus.AwaitingDelivery,
                        OutboxMessageId = pair.First.Id,
                        HostId = intent.HostId,
                        Channel = normalizedChannel,
                        Feature = intent.Feature,
                        ReplyKey = intent.ReplyKey,
                        OwnerId = intent.OwnerId,
                        DurationSeconds = intent.DurationSeconds,
                        UnpinOnOwnerCompletion = intent.UnpinOnOwnerCompletion,
                        TwitchMessageId = string.Empty,
                        CreatedAtUtc = createdAtUtc,
                    }
                );
            }

            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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
