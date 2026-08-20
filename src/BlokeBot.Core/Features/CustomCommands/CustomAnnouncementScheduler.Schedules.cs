using System.Diagnostics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

internal sealed partial class CustomAnnouncementScheduler
{
    private static AnnouncementDueResult EvaluateSchedule(
        CustomAnnouncement announcement,
        AnnouncementCandidate candidate,
        DateTimeOffset now
    ) =>
        announcement.Schedule switch
        {
            IntervalCustomAnnouncementSchedule interval => IsIntervalDue(
                announcement,
                interval.IntervalMinutes,
                now
            ),
            IntervalAfterChatCustomAnnouncementSchedule intervalAfterChat =>
                announcement.ChatMessagesSinceLastSent < intervalAfterChat.RequiredChatMessages
                    ? new AnnouncementDueResult.NotDue()
                    : IsIntervalDue(announcement, intervalAfterChat.IntervalMinutes, now),
            WeeklyCustomAnnouncementSchedule weekly => EvaluateWeeklySchedule(
                announcement,
                candidate,
                weekly,
                now
            ),
            _ => throw new UnreachableException("Unknown custom announcement schedule."),
        };

    private static AnnouncementDueResult IsIntervalDue(
        CustomAnnouncement announcement,
        int intervalMinutes,
        DateTimeOffset now
    )
    {
        var baseline = AsUtc(
            announcement.LastOccurrenceAtUtc
                ?? announcement.LastSentAtUtc
                ?? announcement.CreatedAtUtc
        );
        var dueAt = baseline.AddMinutes(intervalMinutes);
        return dueAt <= now
            ? new AnnouncementDueResult.Due(dueAt)
            : new AnnouncementDueResult.NotDue();
    }

    private static AnnouncementDueResult EvaluateWeeklySchedule(
        CustomAnnouncement announcement,
        AnnouncementCandidate candidate,
        WeeklyCustomAnnouncementSchedule schedule,
        DateTimeOffset now
    )
    {
        var utcNow = now.UtcDateTime;
        if (utcNow.DayOfWeek != schedule.Day)
        {
            return new AnnouncementDueResult.NotDue();
        }

        var dueAtUtc = DateOnly.FromDateTime(utcNow).ToDateTime(schedule.Time, DateTimeKind.Utc);
        if (dueAtUtc > utcNow)
        {
            return new AnnouncementDueResult.NotDue();
        }

        var dueAt = new DateTimeOffset(dueAtUtc, TimeSpan.Zero);
        var hasAlreadyOccurred =
            (announcement.LastOccurrenceAtUtc ?? announcement.LastSentAtUtc) >= dueAt.UtcDateTime;
        var runtimeChanged = candidate.Runtime.ChangedAtUtc > dueAt.UtcDateTime;
        return hasAlreadyOccurred switch
        {
            true => new AnnouncementDueResult.NotDue(),
            false when runtimeChanged => new AnnouncementDueResult.NotDue(),
            false => new AnnouncementDueResult.Due(dueAt),
        };
    }

    private abstract record AnnouncementDueResult
    {
        private AnnouncementDueResult() { }

        internal sealed record Due(DateTimeOffset DueAt) : AnnouncementDueResult;

        internal sealed record NotDue : AnnouncementDueResult;
    }
}
