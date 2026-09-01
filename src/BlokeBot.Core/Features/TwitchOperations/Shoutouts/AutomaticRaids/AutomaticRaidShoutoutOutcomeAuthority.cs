using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

internal sealed class AutomaticRaidShoutoutOutcomeAuthority(EventBus<AppEventKind>? events = null)
{
    private const int _contentionAttemptLimit = 3;
    private static readonly TimeSpan _contentionRetryDelay = TimeSpan.FromMilliseconds(25);

    internal async Task<AutomaticRaidOutcomeTransitionResult> ApplyAsync(
        BlokeBotDbContext db,
        AutomaticRaidOutcomeIdentity identity,
        AutomaticRaidOutcomeTransition transition,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    )
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await ApplyCoreAsync(db, identity, transition, occurredAt, cancellationToken);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction =
                    await MainDatabaseWriteTransaction.StartImmediateAsync(db, cancellationToken);
                var result = await ApplyCoreAsync(
                    db,
                    identity,
                    transition,
                    occurredAt,
                    cancellationToken
                );
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception exception)
                when (IsContention(exception) && attempt < _contentionAttemptLimit)
            {
                await Task.Delay(_contentionRetryDelay, cancellationToken);
            }
        }
    }

    private static async Task<AutomaticRaidOutcomeTransitionResult> ApplyCoreAsync(
        BlokeBotDbContext db,
        AutomaticRaidOutcomeIdentity identity,
        AutomaticRaidOutcomeTransition transition,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    )
    {
        var current = await LoadAsync(db, identity, cancellationToken);
        if (current is null)
        {
            return new AutomaticRaidOutcomeTransitionResult.NotFound();
        }

        var target = Target(transition, occurredAt);
        var changed = await EligibleOutcomes(db, identity, transition)
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(outcome => outcome.Status, target.Status)
                        .SetProperty(outcome => outcome.ResultCode, target.ResultCode)
                        .SetProperty(outcome => outcome.CompletedAtUtc, target.CompletedAtUtc),
                cancellationToken
            );
        var resolved =
            changed == 1 ? target : await LoadAsync(db, identity, cancellationToken) ?? current;
        await ProjectRaidHistoryAsync(db, identity, resolved.ResultCode, cancellationToken);
        return changed == 1
            ? new AutomaticRaidOutcomeTransitionResult.Applied(identity, resolved)
            : new AutomaticRaidOutcomeTransitionResult.Unchanged(identity, resolved);
    }

    internal async Task<AutomaticRaidOutcomeTransitionResult> ApplyCorrelatedAsync(
        BlokeBotDbContext db,
        string deduplicationKey,
        AutomaticRaidOutcomeTransition transition,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    )
    {
        var candidates = await db
            .AutomaticRaidShoutoutOutcomes.AsNoTracking()
            .Select(outcome => new AutomaticRaidOutcomeIdentity(
                outcome.HostId,
                outcome.Id,
                outcome.ProviderMessageId
            ))
            .ToArrayAsync(cancellationToken);
        var identity = candidates.SingleOrDefault(candidate =>
            PublicChatMessageDeduplication
                .CorrelatedKey(
                    new PublicChatDeliveryCorrelation(candidate.HostId, candidate.ProviderMessageId)
                )
                .Value == deduplicationKey
        );
        return identity is null
            ? new AutomaticRaidOutcomeTransitionResult.NotFound()
            : await ApplyAsync(db, identity, transition, occurredAt, cancellationToken);
    }

    internal ValueTask PublishCommittedAsync(AutomaticRaidOutcomeTransitionResult result) =>
        PublishCommittedAsync(result is AutomaticRaidOutcomeTransitionResult.Applied);

    internal async ValueTask PublishCommittedAsync(bool changed)
    {
        if (changed && events is not null)
        {
            _ = await events.PublishAsync(
                AppEventKind.RaidCollaborationChanged,
                CancellationToken.None
            );
        }
    }

    internal static RaidShoutoutOutcome ToRaidShoutoutOutcome(
        AutomaticRaidShoutoutResultCode? resultCode
    ) =>
        resultCode switch
        {
            null => RaidShoutoutOutcome.NotConfigured,
            AutomaticRaidShoutoutResultCode.Queued => RaidShoutoutOutcome.Queued,
            AutomaticRaidShoutoutResultCode.Delivered => RaidShoutoutOutcome.Sent,
            AutomaticRaidShoutoutResultCode.PartialFailure => RaidShoutoutOutcome.Sent,
            AutomaticRaidShoutoutResultCode.Cooldown => RaidShoutoutOutcome.Cooldown,
            AutomaticRaidShoutoutResultCode.Invalid => RaidShoutoutOutcome.NotEligible,
            _ => RaidShoutoutOutcome.Rejected,
        };

    private static IQueryable<AutomaticRaidShoutoutOutcome> EligibleOutcomes(
        BlokeBotDbContext db,
        AutomaticRaidOutcomeIdentity identity,
        AutomaticRaidOutcomeTransition transition
    )
    {
        var identified = db.AutomaticRaidShoutoutOutcomes.Where(outcome =>
            outcome.Id == identity.OutcomeId
            && outcome.HostId == identity.HostId
            && outcome.ProviderMessageId == identity.ProviderMessageId
        );
        return transition switch
        {
            AutomaticRaidOutcomeTransition.QueueAccepted => identified.Where(outcome =>
                outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Processing
            ),
            AutomaticRaidOutcomeTransition.TransportDelivered => identified.Where(outcome =>
                outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Processing
                || outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Queued
            ),
            AutomaticRaidOutcomeTransition.TerminalFailure
            or AutomaticRaidOutcomeTransition.Ambiguous => identified.Where(outcome =>
                outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Processing
                || outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Queued
            ),
            AutomaticRaidOutcomeTransition.PinFailed => identified.Where(outcome =>
                outcome.Status == AutomaticRaidShoutoutOutcomeStatus.Delivered
            ),
            _ => identified.Where(static _ => false),
        };
    }

    private static AutomaticRaidOutcomeState Target(
        AutomaticRaidOutcomeTransition transition,
        DateTimeOffset occurredAt
    ) =>
        transition switch
        {
            AutomaticRaidOutcomeTransition.QueueAccepted => new(
                AutomaticRaidShoutoutOutcomeStatus.Queued,
                AutomaticRaidShoutoutResultCode.Queued,
                null
            ),
            AutomaticRaidOutcomeTransition.TransportDelivered => Terminal(
                AutomaticRaidShoutoutOutcomeStatus.Delivered,
                AutomaticRaidShoutoutResultCode.Delivered,
                occurredAt
            ),
            AutomaticRaidOutcomeTransition.Ambiguous => Terminal(
                AutomaticRaidShoutoutOutcomeStatus.Ambiguous,
                AutomaticRaidShoutoutResultCode.Ambiguous,
                occurredAt
            ),
            AutomaticRaidOutcomeTransition.PinFailed => Terminal(
                AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
                AutomaticRaidShoutoutResultCode.PartialFailure,
                occurredAt
            ),
            AutomaticRaidOutcomeTransition.TerminalFailure failure => Terminal(
                AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
                SafeFailureCode(failure.ResultCode),
                occurredAt
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(transition)),
        };

    private static AutomaticRaidShoutoutResultCode SafeFailureCode(
        AutomaticRaidShoutoutResultCode resultCode
    ) =>
        resultCode
            is AutomaticRaidShoutoutResultCode.Queued
                or AutomaticRaidShoutoutResultCode.Delivered
                or AutomaticRaidShoutoutResultCode.Ambiguous
                or AutomaticRaidShoutoutResultCode.PartialFailure
            ? AutomaticRaidShoutoutResultCode.Unexpected
            : resultCode;

    private static AutomaticRaidOutcomeState Terminal(
        AutomaticRaidShoutoutOutcomeStatus status,
        AutomaticRaidShoutoutResultCode resultCode,
        DateTimeOffset occurredAt
    ) => new(status, resultCode, occurredAt.UtcDateTime);

    private static async Task<AutomaticRaidOutcomeState?> LoadAsync(
        BlokeBotDbContext db,
        AutomaticRaidOutcomeIdentity identity,
        CancellationToken cancellationToken
    ) =>
        await db
            .AutomaticRaidShoutoutOutcomes.AsNoTracking()
            .Where(outcome =>
                outcome.Id == identity.OutcomeId
                && outcome.HostId == identity.HostId
                && outcome.ProviderMessageId == identity.ProviderMessageId
            )
            .Select(outcome => new AutomaticRaidOutcomeState(
                outcome.Status,
                outcome.ResultCode,
                outcome.CompletedAtUtc
            ))
            .SingleOrDefaultAsync(cancellationToken);

    private static async Task ProjectRaidHistoryAsync(
        BlokeBotDbContext db,
        AutomaticRaidOutcomeIdentity identity,
        AutomaticRaidShoutoutResultCode? resultCode,
        CancellationToken cancellationToken
    )
    {
        if (resultCode is null)
        {
            return;
        }

        var projected = ToRaidShoutoutOutcome(resultCode);
        _ = await db
            .RaidCollaborationHistory.Where(history =>
                history.HostId == identity.HostId
                && history.ProviderMessageId == identity.ProviderMessageId
                && history.Direction == RaidDirection.Incoming
                && history.ShoutoutOutcome != projected
            )
            .ExecuteUpdateAsync(
                update => update.SetProperty(history => history.ShoutoutOutcome, projected),
                cancellationToken
            );
    }

    private static bool IsContention(Exception exception) =>
        MainDatabaseFailureClassifier.IsContention(exception);
}
