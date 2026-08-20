namespace BlokeBot.Core.Features.CustomCommands;

public static partial class CustomCommandConfigurationValidator
{
    private static CustomAnnouncementScheduleValue SnapshotSchedule(
        int announcementId,
        string announcementName,
        ICustomAnnouncementScheduleEditor editor,
        TimeZoneInfo timeZone,
        DateTimeOffset projectionReference,
        ICollection<CustomCommandConfigurationValidationError> errors
    )
    {
        switch (editor)
        {
            case IntervalCustomAnnouncementScheduleEditor interval:
                if (interval.IntervalMinutes < 1)
                {
                    AddError(
                        errors,
                        "Announcements must wait at least 1 minute.",
                        AnnouncementTarget(
                            announcementId,
                            CustomCommandValidationFieldKind.Interval
                        )
                    );
                }

                return new CustomAnnouncementScheduleValue.Interval(interval.IntervalMinutes);
            case IntervalAfterChatCustomAnnouncementScheduleEditor intervalAfterChat:
                if (intervalAfterChat.IntervalMinutes < 1)
                {
                    AddError(
                        errors,
                        "Announcements must wait at least 1 minute.",
                        AnnouncementTarget(
                            announcementId,
                            CustomCommandValidationFieldKind.Interval
                        )
                    );
                }

                if (intervalAfterChat.RequiredChatMessages < 1)
                {
                    AddError(
                        errors,
                        "Chat-based announcements need at least 1 chat message.",
                        AnnouncementTarget(
                            announcementId,
                            CustomCommandValidationFieldKind.ChatMessages
                        )
                    );
                }

                return new CustomAnnouncementScheduleValue.IntervalAfterChat(
                    intervalAfterChat.IntervalMinutes,
                    intervalAfterChat.RequiredChatMessages
                );
            case WeeklyCustomAnnouncementScheduleEditor weekly:
                if (!Enum.IsDefined(weekly.Day))
                {
                    AddError(
                        errors,
                        "Choose a valid day for weekly announcements.",
                        AnnouncementTarget(announcementId, CustomCommandValidationFieldKind.Day)
                    );
                }

                var utc = WeeklyAnnouncementScheduleEditorProjection.ToUtc(
                    weekly,
                    timeZone,
                    projectionReference
                );
                return new CustomAnnouncementScheduleValue.Weekly(utc.Day, utc.Time);
            default:
                AddError(
                    errors,
                    $"Choose when announcement '{announcementName}' should be sent.",
                    AnnouncementTarget(announcementId, CustomCommandValidationFieldKind.Schedule)
                );
                return new CustomAnnouncementScheduleValue.Interval(1);
        }
    }

    private static CustomCommandTimeZone NormalizeTimeZone(
        string timeZoneId,
        ICollection<CustomCommandConfigurationValidationError> errors,
        out TimeZoneInfo timeZone
    )
    {
        var normalized = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(normalized, out timeZone!))
        {
            AddError(
                errors,
                $"Time zone '{normalized}' was not found.",
                new(
                    CustomCommandSettingsTab.Commands,
                    CustomCommandValidationEntityKind.Configuration,
                    0,
                    CustomCommandValidationFieldKind.TimeZone
                )
            );
            timeZone = TimeZoneInfo.Utc;
        }

        return new(normalized);
    }
}
