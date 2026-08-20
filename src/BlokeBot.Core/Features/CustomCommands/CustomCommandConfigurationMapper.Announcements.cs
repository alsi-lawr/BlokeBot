using System.Diagnostics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

internal static partial class CustomCommandConfigurationMapper
{
    public static CustomAnnouncementEditor ToEditor(
        CustomAnnouncement announcement,
        TimeZoneInfo timeZone,
        DateTimeOffset referenceUtc
    ) =>
        new()
        {
            Id = announcement.Id,
            Name = announcement.Name,
            Enabled = announcement.Enabled,
            MessageLibraryEntryId = announcement.MessageLibraryEntryId,
            DeliveryType = announcement.DeliveryType,
            AnnouncementColor = announcement.AnnouncementColor,
            LatestDeliveryResult = announcement.LatestDeliveryResult,
            RetryDelaySeconds = ToWholeSeconds(
                RequireRetryUntilExpiredThenSkip(announcement.DeliveryPolicy).RetryDelay.Value
            ),
            OccurrenceLifetimeSeconds = ToWholeSeconds(
                RequireRetryUntilExpiredThenSkip(
                    announcement.DeliveryPolicy
                ).OccurrenceLifetime.Value
            ),
            Schedule = announcement.Schedule switch
            {
                IntervalCustomAnnouncementSchedule schedule =>
                    new IntervalCustomAnnouncementScheduleEditor
                    {
                        IntervalMinutes = schedule.IntervalMinutes,
                    },
                IntervalAfterChatCustomAnnouncementSchedule schedule =>
                    new IntervalAfterChatCustomAnnouncementScheduleEditor
                    {
                        IntervalMinutes = schedule.IntervalMinutes,
                        RequiredChatMessages = schedule.RequiredChatMessages,
                    },
                WeeklyCustomAnnouncementSchedule schedule =>
                    WeeklyAnnouncementScheduleEditorProjection.FromUtc(
                        schedule.Day,
                        schedule.Time,
                        timeZone,
                        referenceUtc
                    ),
                _ => throw new InvalidOperationException(
                    "Unsupported custom announcement schedule."
                ),
            },
            LastSentAtUtc = announcement.LastSentAtUtc,
            ChatMessagesSinceLastSent = announcement.ChatMessagesSinceLastSent,
        };

    public static CustomAnnouncementDeliveryPolicy CreateDeliveryPolicy(
        int hostId,
        CustomAnnouncementValue announcement
    ) =>
        new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
        {
            HostId = hostId,
            RetryDelay = announcement.RetryDelay,
            OccurrenceLifetime = announcement.OccurrenceLifetime,
        };

    public static void ApplyDeliveryPolicy(
        CustomAnnouncementDeliveryPolicy policy,
        CustomAnnouncementValue announcement
    )
    {
        var retry = RequireRetryUntilExpiredThenSkip(policy);
        retry.RetryDelay = announcement.RetryDelay;
        retry.OccurrenceLifetime = announcement.OccurrenceLifetime;
    }

    public static CustomAnnouncementSchedule CreateSchedule(
        int hostId,
        CustomAnnouncementScheduleValue schedule
    ) =>
        schedule switch
        {
            CustomAnnouncementScheduleValue.Interval => new IntervalCustomAnnouncementSchedule
            {
                HostId = hostId,
            },
            CustomAnnouncementScheduleValue.IntervalAfterChat =>
                new IntervalAfterChatCustomAnnouncementSchedule { HostId = hostId },
            CustomAnnouncementScheduleValue.Weekly => new WeeklyCustomAnnouncementSchedule
            {
                HostId = hostId,
            },
            _ => throw new InvalidOperationException("Unsupported custom announcement schedule."),
        };

    public static void ApplySchedule(
        CustomAnnouncementSchedule schedule,
        CustomAnnouncementScheduleValue value
    )
    {
        switch (schedule, value)
        {
            case (
                IntervalCustomAnnouncementSchedule stored,
                CustomAnnouncementScheduleValue.Interval configured
            ):
                stored.IntervalMinutes = configured.IntervalMinutes;
                return;
            case (
                IntervalAfterChatCustomAnnouncementSchedule stored,
                CustomAnnouncementScheduleValue.IntervalAfterChat configured
            ):
                stored.IntervalMinutes = configured.IntervalMinutes;
                stored.RequiredChatMessages = configured.RequiredChatMessages;
                return;
            case (
                WeeklyCustomAnnouncementSchedule stored,
                CustomAnnouncementScheduleValue.Weekly configured
            ):
                stored.Day = configured.Day;
                stored.Time = configured.Time;
                return;
            default:
                throw new InvalidOperationException(
                    "Custom announcement schedule types do not match."
                );
        }
    }

    public static bool ScheduleMatches(
        CustomAnnouncementSchedule schedule,
        CustomAnnouncementScheduleValue value
    ) =>
        (schedule, value)
            is
                (IntervalCustomAnnouncementSchedule, CustomAnnouncementScheduleValue.Interval)
                or
                (
                    IntervalAfterChatCustomAnnouncementSchedule,
                    CustomAnnouncementScheduleValue.IntervalAfterChat
                )
                or
                (WeeklyCustomAnnouncementSchedule, CustomAnnouncementScheduleValue.Weekly);

    private static RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy RequireRetryUntilExpiredThenSkip(
        CustomAnnouncementDeliveryPolicy policy
    ) =>
        policy as RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
        ?? throw new UnreachableException("Unknown custom announcement delivery policy.");

    private static int ToWholeSeconds(TimeSpan value) =>
        value.Ticks % TimeSpan.TicksPerSecond != 0
            ? throw new InvalidOperationException(
                "Announcement delivery timing must use whole seconds."
            )
            : checked((int)(value.Ticks / TimeSpan.TicksPerSecond));
}
