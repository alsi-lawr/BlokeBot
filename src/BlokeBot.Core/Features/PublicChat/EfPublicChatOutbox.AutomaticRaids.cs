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
        DurableAlertService.ReportOperation? reportOperation,
        BlokeBotDbContext db,
        PublicChatClaimedMessage message,
        AutomaticRaidShoutoutResultCode resultCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken
    ) =>
        RecordAutomaticRaidTerminalAsync(
            reportOperation,
            db,
            message.DeduplicationKey.Value,
            message.Id,
            resultCode,
            completedAt,
            cancellationToken
        );

    private async Task<DurableAlertPendingChange?> RecordAutomaticRaidTerminalAsync(
        DurableAlertService.ReportOperation? reportOperation,
        BlokeBotDbContext db,
        string deduplicationKey,
        long outboxMessageId,
        AutomaticRaidShoutoutResultCode resultCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken
    )
    {
        AutomaticRaidOutcomeTransition transition =
            resultCode is AutomaticRaidShoutoutResultCode.Ambiguous
                ? new AutomaticRaidOutcomeTransition.Ambiguous()
                : new AutomaticRaidOutcomeTransition.TerminalFailure(resultCode);
        var recorded = await _automaticRaidOutcomes.ApplyCorrelatedAsync(
            db,
            deduplicationKey,
            transition,
            completedAt,
            cancellationToken
        );
        if (recorded is not AutomaticRaidOutcomeTransitionResult.Applied applied)
        {
            return null;
        }

        _ = await db
            .PublicChatPinOperations.Where(operation =>
                operation.OutboxMessageId == outboxMessageId
                && operation.Status == PublicChatPinOperationStatus.AwaitingDelivery
                && operation.HostId == applied.Identity.HostId
                && operation.Feature == AutomaticRaidDeliveryCorrelation.Feature
                && operation.OwnerId == applied.Identity.OutcomeId
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

        _ =
            alerts
            ?? throw new InvalidOperationException(
                "Automatic raid terminal outcomes require the durable alert authority."
            );
        return await (
            reportOperation
            ?? throw new InvalidOperationException(
                "Automatic raid terminal outcomes require an active alert report operation."
            )
        ).StageAsync(
            db,
            new DurableAlertReport(
                new DurableAlertIdentity(
                    applied.Identity.HostId,
                    AutomaticRaidDeliveryCorrelation.AlertSource,
                    applied.Identity.ProviderMessageId
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

    private async ValueTask PublishCommittedAlertAsync(
        DurableAlertService.ReportOperation? reportOperation,
        DurableAlertPendingChange? change
    )
    {
        if (change is not null)
        {
            await reportOperation!.PublishCommittedAsync(change);
            await _automaticRaidOutcomes.PublishCommittedAsync(changed: true);
        }
    }

    private async ValueTask PublishCommittedAlertsAsync(
        DurableAlertService.ReportOperation? reportOperation,
        IEnumerable<DurableAlertPendingChange> changes
    )
    {
        var changed = false;
        foreach (var change in changes)
        {
            await reportOperation!.PublishCommittedAsync(change);
            changed = true;
        }
        await _automaticRaidOutcomes.PublishCommittedAsync(changed);
    }

    private async ValueTask<DurableAlertService.ReportOperation?> BeginAlertReportOperationAsync(
        CancellationToken cancellationToken
    ) => alerts is null ? null : await alerts.BeginReportOperationAsync(cancellationToken);
}
