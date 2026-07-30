using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PublicChat;

internal sealed class EfPublicChatPinStore(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider timeProvider,
    EventBus<AppEventKind> events
) : IPublicChatPinStore
{
    public async ValueTask<PublicChatPinWorkItem?> TryClaimAsync(
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var recovering = await db
            .PublicChatPinOperations.AsNoTracking()
            .Where(operation => operation.Status == PublicChatPinOperationStatus.Attempting)
            .OrderBy(operation => operation.AttemptStartedAtUtc)
            .ThenBy(operation => operation.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (recovering is not null)
        {
            return ToWorkItem(recovering, true);
        }

        var ready = await db
            .PublicChatPinOperations.AsNoTracking()
            .Where(operation => operation.Status == PublicChatPinOperationStatus.Ready)
            .OrderBy(operation => operation.CreatedAtUtc)
            .ThenBy(operation => operation.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return ready is null ? null : ToWorkItem(ready, false);
    }

    public async ValueTask<bool> BeginAttemptAsync(
        PublicChatPinWorkItem item,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
                .PublicChatPinOperations.Where(operation =>
                    operation.Id == item.Id
                    && operation.Status == PublicChatPinOperationStatus.Ready
                )
                .ExecuteUpdateAsync(
                    update =>
                        update
                            .SetProperty(
                                operation => operation.Status,
                                PublicChatPinOperationStatus.Attempting
                            )
                            .SetProperty(operation => operation.AttemptStartedAtUtc, UtcNow()),
                    cancellationToken
                ) == 1;
    }

    public async ValueTask CompleteAsync(
        PublicChatPinWorkItem item,
        PublicChatPinExecutionOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var operation = await db.PublicChatPinOperations.SingleOrDefaultAsync(
            row => row.Id == item.Id && row.Status == PublicChatPinOperationStatus.Attempting,
            cancellationToken
        );
        if (operation is null)
        {
            return;
        }

        operation.CompletedAtUtc = UtcNow();
        switch (outcome)
        {
            case PublicChatPinExecutionOutcome.Pinned pinned:
                operation.Status = PublicChatPinOperationStatus.Succeeded;
                operation.PinnerTwitchUserId = pinned.PinnerTwitchUserId;
                operation.Outcome = "pinned";
                await RecordActivePinAsync(db, item, pinned.PinnerTwitchUserId, cancellationToken);
                break;
            case PublicChatPinExecutionOutcome.Unpinned:
                operation.Status = PublicChatPinOperationStatus.Succeeded;
                operation.Outcome = "unpinned";
                await RemoveOwnershipAsync(db, item, cancellationToken);
                break;
            case PublicChatPinExecutionOutcome.NoOp noOp:
                operation.Status = PublicChatPinOperationStatus.NoOp;
                operation.Outcome = noOp.Reason;
                if (item.IsUnpin)
                {
                    await RemoveOwnershipAsync(db, item, cancellationToken);
                }
                break;
            case PublicChatPinExecutionOutcome.Terminal terminal:
                operation.Status = PublicChatPinOperationStatus.Terminal;
                operation.Outcome = terminal.Reason;
                await RecordAutomaticRaidPartialFailureAsync(db, item, cancellationToken);
                await AddAlertAsync(db, item, terminal.Reason, cancellationToken);
                break;
            default:
                throw new InvalidOperationException("Unknown public chat pin outcome.");
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (outcome is PublicChatPinExecutionOutcome.Terminal)
        {
            await events.PublishAsync(AppEventKind.AlertsChanged, cancellationToken);
        }
    }

    private async Task RecordActivePinAsync(
        BlokeBotDbContext db,
        PublicChatPinWorkItem item,
        string pinnerTwitchUserId,
        CancellationToken cancellationToken
    )
    {
        var active = await db.ActivePublicChatPins.SingleOrDefaultAsync(
            pin => pin.HostId == item.HostId && pin.Channel == item.Channel,
            cancellationToken
        );
        if (active is null)
        {
            active = new ActivePublicChatPin
            {
                HostId = item.HostId,
                Channel = item.Channel,
                TwitchMessageId = item.TwitchMessageId,
                PinnerTwitchUserId = pinnerTwitchUserId,
                Feature = item.Feature,
                ReplyKey = item.ReplyKey,
                OwnerId = item.OwnerId,
                UnpinOnOwnerCompletion = item.UnpinOnOwnerCompletion,
                PinnedAtUtc = UtcNow(),
            };
            db.ActivePublicChatPins.Add(active);
        }
        else
        {
            active.TwitchMessageId = item.TwitchMessageId;
            active.PinnerTwitchUserId = pinnerTwitchUserId;
            active.Feature = item.Feature;
            active.ReplyKey = item.ReplyKey;
            active.OwnerId = item.OwnerId;
            active.UnpinOnOwnerCompletion = item.UnpinOnOwnerCompletion;
            active.PinnedAtUtc = UtcNow();
        }

        var ownerStillOpen =
            item.Feature != "guessing"
            || await db.Rounds.AnyAsync(
                round =>
                    round.Id == item.OwnerId
                    && round.HostId == item.HostId
                    && round.Status == GuessRoundStatus.Open,
                cancellationToken
            );
        if (!ownerStillOpen && item.UnpinOnOwnerCompletion)
        {
            db.PublicChatPinOperations.Add(
                new PublicChatPinOperation
                {
                    Kind = PublicChatPinOperationKind.Unpin,
                    Status = PublicChatPinOperationStatus.Ready,
                    HostId = item.HostId,
                    Channel = item.Channel,
                    Feature = item.Feature,
                    ReplyKey = item.ReplyKey,
                    OwnerId = item.OwnerId,
                    TwitchMessageId = item.TwitchMessageId,
                    PinnerTwitchUserId = pinnerTwitchUserId,
                    CreatedAtUtc = UtcNow(),
                }
            );
        }
    }

    private static async Task RemoveOwnershipAsync(
        BlokeBotDbContext db,
        PublicChatPinWorkItem item,
        CancellationToken cancellationToken
    )
    {
        await db
            .ActivePublicChatPins.Where(pin =>
                pin.HostId == item.HostId
                && pin.Channel == item.Channel
                && pin.Feature == item.Feature
                && pin.OwnerId == item.OwnerId
                && pin.TwitchMessageId == item.TwitchMessageId
                && pin.PinnerTwitchUserId == item.RecordedPinnerTwitchUserId
            )
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task AddAlertAsync(
        BlokeBotDbContext db,
        PublicChatPinWorkItem item,
        string reason,
        CancellationToken cancellationToken
    )
    {
        var automaticRaid = item.Feature == AutomaticRaidDeliveryCorrelation.Feature;
        var source = automaticRaid
            ? AutomaticRaidDeliveryCorrelation.AlertSource
            : "public-chat-pin";
        var sourceKey = automaticRaid ? item.ReplyKey : $"{item.Id}:{reason}";
        if (
            await db.DurableAlerts.AnyAsync(
                alert =>
                    alert.HostId == item.HostId
                    && alert.Source == source
                    && alert.SourceKey == sourceKey
                    && alert.AcknowledgedAtUtc == null,
                cancellationToken
            )
        )
        {
            return;
        }

        db.DurableAlerts.Add(
            new DurableAlert
            {
                HostId = item.HostId,
                Severity = DurableAlertSeverity.Warning,
                Source = source,
                SourceKey = sourceKey,
                Title =
                    automaticRaid ? "Automatic raid shoutout pin failed"
                    : item.IsUnpin ? "Chat pin reset failed"
                    : "Chat reply pin failed",
                Message =
                    $"Twitch could not complete the chat pin operation ({reason}). Check the bot moderator role and reconnect its account if the required scope is missing.",
                LinkPath = "/channel/setup",
                CreatedAtUtc = UtcNow(),
            }
        );
    }

    private async Task RecordAutomaticRaidPartialFailureAsync(
        BlokeBotDbContext db,
        PublicChatPinWorkItem item,
        CancellationToken cancellationToken
    )
    {
        if (item.Feature != AutomaticRaidDeliveryCorrelation.Feature)
        {
            return;
        }

        var outcome = await db.AutomaticRaidShoutoutOutcomes.SingleOrDefaultAsync(
            value =>
                value.Id == item.OwnerId
                && value.HostId == item.HostId
                && value.ProviderMessageId == item.ReplyKey,
            cancellationToken
        );
        if (outcome is null)
        {
            return;
        }

        outcome.Status = AutomaticRaidShoutoutOutcomeStatus.NotDelivered;
        outcome.ResultCode = AutomaticRaidShoutoutResultCode.PartialFailure;
        outcome.CompletedAtUtc = UtcNow();
    }

    private static PublicChatPinWorkItem ToWorkItem(
        PublicChatPinOperation operation,
        bool reconcileOnly
    )
    {
        return new PublicChatPinWorkItem(
            operation.Id,
            reconcileOnly,
            operation.Kind == PublicChatPinOperationKind.Unpin,
            operation.HostId,
            operation.Channel,
            operation.Feature,
            operation.ReplyKey,
            operation.OwnerId,
            operation.TwitchMessageId,
            operation.PinnerTwitchUserId,
            operation.DurationSeconds,
            operation.UnpinOnOwnerCompletion
        );
    }

    private DateTime UtcNow()
    {
        return timeProvider.GetUtcNow().UtcDateTime;
    }
}
