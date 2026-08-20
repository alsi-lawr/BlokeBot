using System.Diagnostics;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed partial class CustomAnnouncementScheduler
{
    private async Task ProcessCandidateAsync(
        AnnouncementCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var announcement = await db
            .CustomAnnouncements.Include(x => x.Schedule)
            .Include(x => x.DeliveryPolicy)
            .Include(x => x.MessageLibraryEntry)
                .ThenInclude(x => x!.Variants)
            .SingleOrDefaultAsync(x => x.Id == candidate.AnnouncementId, cancellationToken);
        if (announcement is null || !announcement.Enabled)
        {
            return;
        }

        var policy = AnnouncementDeliveryPolicyMapper.ToDomain(announcement.DeliveryPolicy);
        await ProcessRetryUntilExpiredThenSkipAsync(
            db,
            announcement,
            candidate,
            policy,
            now,
            cancellationToken
        );
    }

    private async Task ProcessRetryUntilExpiredThenSkipAsync(
        BlokeBotDbContext db,
        CustomAnnouncement announcement,
        AnnouncementCandidate candidate,
        AnnouncementDeliveryPolicy policy,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (announcement.OccurrenceStatus == AnnouncementOccurrenceStatus.Attempting)
        {
            CompleteOccurrence(announcement, AnnouncementOccurrenceStatus.TerminalAmbiguous, now);
            announcement.LatestDeliveryResult = CustomAnnouncementLatestDeliveryResult.Ambiguous;
            _ = await db.SaveChangesAsync(cancellationToken);
            LogTerminal(announcement, candidate, "InterruptedAttempt");
            return;
        }

        if (announcement.OccurrenceStatus == AnnouncementOccurrenceStatus.TerminalInvalidTimeZone)
        {
            ResetOccurrence(announcement);
            _ = await db.SaveChangesAsync(cancellationToken);
        }

        var due = EvaluateSchedule(announcement, candidate, now);
        if (due is AnnouncementDueResult.NotDue)
        {
            return;
        }

        var dueAt = ((AnnouncementDueResult.Due)due).DueAt;
        if (announcement.OccurrenceDueAtUtc != dueAt.UtcDateTime)
        {
            StartOccurrence(announcement, dueAt, policy.OccurrenceLifetime);
            _ = await db.SaveChangesAsync(cancellationToken);
        }

        var expiresAt = RequireUtc(announcement.OccurrenceExpiresAtUtc, "expiry");
        if (now >= expiresAt)
        {
            CompleteOccurrence(announcement, AnnouncementOccurrenceStatus.SkippedExpired, now);
            _ = await db.SaveChangesAsync(cancellationToken);
            LogTerminal(announcement, candidate, "Expired");
            return;
        }

        if (
            announcement.OccurrenceStatus == AnnouncementOccurrenceStatus.RetryScheduled
            && now < RequireUtc(announcement.OccurrenceNextAttemptAtUtc, "next attempt")
        )
        {
            return;
        }

        if (
            announcement.OccurrenceStatus
            is not (
                AnnouncementOccurrenceStatus.Pending
                or AnnouncementOccurrenceStatus.RetryScheduled
            )
        )
        {
            return;
        }

        var selectedMessage = announcement.OccurrenceStatus switch
        {
            AnnouncementOccurrenceStatus.Pending => SelectMessage(
                announcement.MessageLibraryEntry,
                now
            ),
            AnnouncementOccurrenceStatus.RetryScheduled => announcement.OccurrenceMessage
                ?? throw new UnreachableException(
                    "A retry-scheduled announcement occurrence requires its selected message."
                ),
            _ => throw new UnreachableException(
                "Only pending or retry-scheduled occurrences can enqueue."
            ),
        };
        var message =
            announcement.OccurrenceStatus == AnnouncementOccurrenceStatus.Pending
            && selectedMessage is not null
                ? await templates.RenderScheduledAsync(
                    selectedMessage,
                    new(candidate.HostId, candidate.HostLogin, candidate.TwitchUserId),
                    cancellationToken
                )
                : selectedMessage;
        if (string.IsNullOrWhiteSpace(message))
        {
            CompleteOccurrence(
                announcement,
                AnnouncementOccurrenceStatus.TerminalMissingMessage,
                now
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            LogTerminal(announcement, candidate, "MissingMessage");
            return;
        }

        announcement.OccurrenceStatus = AnnouncementOccurrenceStatus.Attempting;
        announcement.OccurrenceAttemptCount = checked(announcement.OccurrenceAttemptCount + 1);
        announcement.OccurrenceNextAttemptAtUtc = null;
        announcement.OccurrenceMessage = message;
        announcement.UpdatedAtUtc = now.UtcDateTime;
        _ = await db.SaveChangesAsync(cancellationToken);

        var outcome = await sender.EnqueueAsync(
            new CustomAnnouncementDeliveryRequest(
                candidate.HostLogin,
                message,
                expiresAt,
                announcement.DeliveryType,
                announcement.AnnouncementColor
            ),
            cancellationToken
        );
        announcement.LatestDeliveryResult = outcome switch
        {
            AnnouncementEnqueueOutcome.Accepted accepted => accepted.LatestDeliveryResult,
            AnnouncementEnqueueOutcome.SafePreEnqueueTransient transient =>
                transient.LatestDeliveryResult,
            AnnouncementEnqueueOutcome.Rejected rejected => rejected.LatestDeliveryResult,
            AnnouncementEnqueueOutcome.Ambiguous ambiguous => ambiguous.LatestDeliveryResult,
            AnnouncementEnqueueOutcome.Unexpected unexpected => unexpected.LatestDeliveryResult,
            _ => throw new UnreachableException("Unknown announcement enqueue outcome."),
        };
        string? terminalReason = null;
        switch (outcome)
        {
            case AnnouncementEnqueueOutcome.Accepted:
                CompleteOccurrence(announcement, AnnouncementOccurrenceStatus.Accepted, now);
                announcement.LastSentAtUtc = dueAt.UtcDateTime;
                announcement.ChatMessagesSinceLastSent = 0;
                break;
            case AnnouncementEnqueueOutcome.SafePreEnqueueTransient transient:
                ScheduleRetry(announcement, policy.RetryDelay, now, expiresAt);
                log.LogWarning(
                    "Custom announcement {AnnouncementId} scheduled safe pre-enqueue retry for host {HostLogin}; Attempt: {Attempt}; FailureType: {FailureType}; NextAttemptAtUtc: {NextAttemptAtUtc}.",
                    announcement.Id,
                    candidate.HostLogin,
                    announcement.OccurrenceAttemptCount,
                    transient.FailureType.Value,
                    announcement.OccurrenceNextAttemptAtUtc
                );
                break;
            case AnnouncementEnqueueOutcome.Rejected:
                CompleteOccurrence(
                    announcement,
                    AnnouncementOccurrenceStatus.TerminalRejected,
                    now
                );
                terminalReason = "Rejected";
                break;
            case AnnouncementEnqueueOutcome.Ambiguous ambiguous:
                CompleteOccurrence(
                    announcement,
                    AnnouncementOccurrenceStatus.TerminalAmbiguous,
                    now
                );
                terminalReason = ambiguous.FailureType.Value;
                break;
            case AnnouncementEnqueueOutcome.Unexpected unexpected:
                CompleteOccurrence(
                    announcement,
                    AnnouncementOccurrenceStatus.TerminalUnexpected,
                    now
                );
                terminalReason = unexpected.FailureType.Value;
                break;
            default:
                throw new UnreachableException("Unknown announcement enqueue outcome.");
        }

        _ = await db.SaveChangesAsync(cancellationToken);
        if (IsTerminal(announcement.OccurrenceStatus))
        {
            LogTerminal(
                announcement,
                candidate,
                terminalReason ?? announcement.OccurrenceStatus.ToString()
            );
        }
    }
}
