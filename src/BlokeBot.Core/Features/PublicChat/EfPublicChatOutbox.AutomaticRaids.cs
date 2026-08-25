using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PublicChat;

internal sealed partial class EfPublicChatOutbox
{
    private static AutomaticRaidShoutoutResultCode SafePreSendExhaustionResult(
        PublicChatHttpStatus httpStatus
    ) =>
        httpStatus.Match(
            static known => SafePreSendExhaustionResult(known.Value),
            static () => AutomaticRaidShoutoutResultCode.Unexpected
        );

    private static AutomaticRaidShoutoutResultCode SafePreSendExhaustionResult(
        int? httpStatusCode
    ) =>
        httpStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests
            ? AutomaticRaidShoutoutResultCode.RateLimited
            : AutomaticRaidShoutoutResultCode.Unexpected;

    private Task<DurableAlertPendingChange?> RecordAutomaticRaidTerminalAsync(
        BlokeBotDbContext db,
        PublicChatClaimedMessage message,
        AutomaticRaidShoutoutResultCode resultCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken
    ) =>
        RecordAutomaticRaidTerminalAsync(
            db,
            message.DeduplicationKey.Value,
            message.Id,
            resultCode,
            completedAt,
            cancellationToken
        );

    private async Task<DurableAlertPendingChange?> RecordAutomaticRaidTerminalAsync(
        BlokeBotDbContext db,
        string deduplicationKey,
        long outboxMessageId,
        AutomaticRaidShoutoutResultCode resultCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken
    )
    {
        var candidates = await db
            .AutomaticRaidShoutoutOutcomes.Where(outcome =>
                outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Processing
                || outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Delivered
            )
            .ToArrayAsync(cancellationToken);
        var outcome = candidates.SingleOrDefault(candidate =>
            PublicChatMessageDeduplication
                .CorrelatedKey(
                    new PublicChatDeliveryCorrelation(candidate.HostId, candidate.ProviderMessageId)
                )
                .Value == deduplicationKey
        );
        if (outcome is null)
        {
            return null;
        }

        outcome.Status =
            resultCode is AutomaticRaidShoutoutResultCode.Ambiguous
                ? AutomaticRaidShoutoutOutcomeStatus.Ambiguous
                : AutomaticRaidShoutoutOutcomeStatus.NotDelivered;
        outcome.ResultCode = resultCode;
        outcome.CompletedAtUtc = completedAt.UtcDateTime;
        _ = await db
            .PublicChatPinOperations.Where(operation =>
                operation.OutboxMessageId == outboxMessageId
                && operation.Status == PublicChatPinOperationStatus.AwaitingDelivery
                && operation.HostId == outcome.HostId
                && operation.Feature == AutomaticRaidDeliveryCorrelation.Feature
                && operation.OwnerId == outcome.Id
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(
                            operation => operation.Status,
                            PublicChatPinOperationStatus.Terminal
                        )
                        .SetProperty(operation => operation.OutboxMessageId, (long?)null)
                        .SetProperty(operation => operation.CompletedAtUtc, completedAt.UtcDateTime)
                        .SetProperty(operation => operation.Outcome, "message-not-delivered"),
                cancellationToken
            );

        var alertService =
            alerts
            ?? throw new InvalidOperationException(
                "Automatic raid terminal outcomes require the durable alert authority."
            );
        return await alertService.StageReportAsync(
            db,
            new DurableAlertReport(
                new DurableAlertIdentity(
                    outcome.HostId,
                    AutomaticRaidDeliveryCorrelation.AlertSource,
                    outcome.ProviderMessageId
                ),
                DurableAlertSeverity.Warning,
                "Automatic raid shoutout was not delivered",
                $"The durable chat delivery ended with {resultCode}. Check the shoutout delivery settings and Twitch connection.",
                "/raid-collaboration",
                completedAt.UtcDateTime
            ),
            cancellationToken
        );
    }

    private async ValueTask PublishCommittedAlertAsync(DurableAlertPendingChange? change)
    {
        if (change is not null)
        {
            await alerts!.PublishCommittedAsync(change);
        }
    }

    private async ValueTask PublishCommittedAlertsAsync(
        IEnumerable<DurableAlertPendingChange> changes
    )
    {
        foreach (var change in changes)
        {
            await alerts!.PublishCommittedAsync(change);
        }
    }
}
