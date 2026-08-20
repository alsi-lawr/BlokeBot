using System.Diagnostics;
using BlokeBot.Announcements;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed partial class CustomAnnouncementScheduler
{
    private static void StartOccurrence(
        CustomAnnouncement announcement,
        DateTimeOffset dueAt,
        AnnouncementOccurrenceLifetime lifetime
    )
    {
        announcement.OccurrenceStatus = AnnouncementOccurrenceStatus.Pending;
        announcement.OccurrenceDueAtUtc = dueAt.UtcDateTime;
        announcement.OccurrenceExpiresAtUtc = dueAt.Add(lifetime.Value).UtcDateTime;
        announcement.OccurrenceNextAttemptAtUtc = dueAt.UtcDateTime;
        announcement.OccurrenceCompletedAtUtc = null;
        announcement.OccurrenceAttemptCount = 0;
        announcement.OccurrenceMessage = null;
    }

    private static void ScheduleRetry(
        CustomAnnouncement announcement,
        AnnouncementRetryDelay retryDelay,
        DateTimeOffset now,
        DateTimeOffset expiresAt
    )
    {
        var nextAttempt = now.Add(retryDelay.Value);
        announcement.OccurrenceStatus = AnnouncementOccurrenceStatus.RetryScheduled;
        announcement.OccurrenceNextAttemptAtUtc = Min(nextAttempt, expiresAt).UtcDateTime;
        announcement.UpdatedAtUtc = now.UtcDateTime;
    }

    private static void CompleteOccurrence(
        CustomAnnouncement announcement,
        AnnouncementOccurrenceStatus status,
        DateTimeOffset completedAt
    )
    {
        if (announcement.OccurrenceDueAtUtc is { } dueAt)
        {
            announcement.LastOccurrenceAtUtc = dueAt;
        }

        announcement.OccurrenceStatus = status;
        announcement.OccurrenceNextAttemptAtUtc = null;
        announcement.OccurrenceCompletedAtUtc = completedAt.UtcDateTime;
        announcement.OccurrenceMessage = null;
        announcement.UpdatedAtUtc = completedAt.UtcDateTime;
    }

    private static void ResetOccurrence(CustomAnnouncement announcement)
    {
        announcement.OccurrenceStatus = AnnouncementOccurrenceStatus.None;
        announcement.OccurrenceDueAtUtc = null;
        announcement.OccurrenceExpiresAtUtc = null;
        announcement.OccurrenceNextAttemptAtUtc = null;
        announcement.OccurrenceCompletedAtUtc = null;
        announcement.OccurrenceAttemptCount = 0;
        announcement.OccurrenceMessage = null;
    }

    private void LogTerminal(
        CustomAnnouncement announcement,
        AnnouncementCandidate candidate,
        string reason
    ) =>
        log.LogWarning(
            "Custom announcement {AnnouncementId} occurrence completed for host {HostLogin}; Status: {Status}; Reason: {Reason}; AttemptCount: {AttemptCount}; DueAtUtc: {DueAtUtc}; ExpiresAtUtc: {ExpiresAtUtc}.",
            announcement.Id,
            candidate.HostLogin,
            announcement.OccurrenceStatus,
            reason,
            announcement.OccurrenceAttemptCount,
            announcement.OccurrenceDueAtUtc,
            announcement.OccurrenceExpiresAtUtc
        );

    private static bool IsTerminal(AnnouncementOccurrenceStatus status) =>
        status
            is AnnouncementOccurrenceStatus.SkippedExpired
                or AnnouncementOccurrenceStatus.TerminalRejected
                or AnnouncementOccurrenceStatus.TerminalAmbiguous
                or AnnouncementOccurrenceStatus.TerminalUnexpected
                or AnnouncementOccurrenceStatus.TerminalInvalidTimeZone
                or AnnouncementOccurrenceStatus.TerminalMissingMessage;

    private TimeSpan TickInterval() =>
        TimeSpan.FromSeconds(
            Math.Max(1, options.Value.CustomCommands.AnnouncementSchedulerTickSeconds)
        );

    private static DateTimeOffset RequireUtc(DateTime? value, string field) =>
        value is { } dateTime
            ? AsUtc(dateTime)
            : throw new UnreachableException($"Announcement occurrence {field} is required.");

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);

    private string? SelectMessage(CustomMessageLibraryEntry? entry, DateTimeOffset selectedAt)
    {
        if (entry is null)
        {
            return null;
        }

        var snapshot = new CustomMessageSelectionSnapshot(
            entry.SelectionMode,
            entry.CurrentVariantIndex,
            entry.Variants.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => x.Text)
        );
        return messageSelector
            .Select(snapshot)
            .Match<string?>(
                selected =>
                {
                    if (entry.SelectionMode is CustomMessageSelectionMode.Sequential)
                    {
                        entry.CurrentVariantIndex = selected.NextVariantIndex;
                        entry.UpdatedAtUtc = selectedAt.UtcDateTime;
                    }

                    return selected.Text;
                },
                static () => null
            );
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private sealed record AnnouncementCandidate(
        int AnnouncementId,
        int HostId,
        string HostLogin,
        string TwitchUserId,
        HostedChannelRuntimeLifecycle.Started Runtime
    );
}
