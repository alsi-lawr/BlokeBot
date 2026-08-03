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

    private static async Task<bool> RecordAutomaticRaidTerminalAsync(
        BlokeBotDbContext db,
        PublicChatClaimedMessage message,
        AutomaticRaidShoutoutResultCode resultCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken
    ) =>
        await RecordAutomaticRaidTerminalAsync(
            db,
            message.DeduplicationKey.Value,
            message.Id,
            resultCode,
            completedAt,
            cancellationToken
        );

    private static async Task<bool> RecordAutomaticRaidTerminalAsync(
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
            return false;
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

        if (
            await db.DurableAlerts.AnyAsync(
                alert =>
                    alert.HostId == outcome.HostId
                    && alert.Source == AutomaticRaidDeliveryCorrelation.AlertSource
                    && alert.SourceKey == outcome.ProviderMessageId
                    && alert.AcknowledgedAtUtc == null,
                cancellationToken
            )
        )
        {
            return false;
        }

        _ = db.DurableAlerts.Add(
            new DurableAlert
            {
                HostId = outcome.HostId,
                Severity = DurableAlertSeverity.Warning,
                Source = AutomaticRaidDeliveryCorrelation.AlertSource,
                SourceKey = outcome.ProviderMessageId,
                Title = "Automatic raid shoutout was not delivered",
                Message =
                    $"The durable chat delivery ended with {resultCode}. Check the shoutout delivery settings and Twitch connection.",
                LinkPath = "/twitch-operations/shoutouts",
                CreatedAtUtc = completedAt.UtcDateTime,
            }
        );
        return true;
    }
}
