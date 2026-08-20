using BlokeBot.Announcements;

namespace BlokeBot.Core.Features.CustomCommands;

internal static class WeeklyAnnouncementScheduleEditorProjection
{
    public static WeeklyCustomAnnouncementScheduleEditor FromUtc(
        DayOfWeek day,
        TimeOnly time,
        TimeZoneInfo timeZone,
        DateTimeOffset referenceUtc
    )
    {
        var utc = new WeeklyAnnouncementSchedule(day, time);
        var local = WeeklyAnnouncementScheduleProjection.ToLocal(utc, timeZone, referenceUtc);
        return new()
        {
            Day = local.Day,
            Time = local.Time,
            ProjectedDay = local.Day,
            ProjectedTime = local.Time,
            UtcDay = utc.Day,
            UtcTime = utc.Time,
            HasUtcSchedule = true,
        };
    }

    public static WeeklyAnnouncementSchedule ToUtc(
        WeeklyCustomAnnouncementScheduleEditor editor,
        TimeZoneInfo timeZone,
        DateTimeOffset referenceUtc
    ) =>
        editor.HasUtcSchedule
        && editor.Day == editor.ProjectedDay
        && editor.Time == editor.ProjectedTime
            ? new(editor.UtcDay, editor.UtcTime)
            : WeeklyAnnouncementScheduleProjection.ToUtc(
                new(editor.Day, editor.Time),
                timeZone,
                referenceUtc
            );

    public static void ChangeTimeZone(CustomCommandConfiguration configuration, string timeZoneId)
    {
        if (
            !TimeZoneInfo.TryFindSystemTimeZoneById(configuration.TimeZoneId, out var previous)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var next)
        )
        {
            configuration.TimeZoneId = timeZoneId;
            return;
        }

        foreach (
            var editor in configuration
                .Announcements.Select(x => x.Schedule)
                .OfType<WeeklyCustomAnnouncementScheduleEditor>()
        )
        {
            var utc = ToUtc(editor, previous, configuration.ProjectionReferenceUtc);
            var local = WeeklyAnnouncementScheduleProjection.ToLocal(
                utc,
                next,
                configuration.ProjectionReferenceUtc
            );
            editor.Day = local.Day;
            editor.Time = local.Time;
            editor.ProjectedDay = local.Day;
            editor.ProjectedTime = local.Time;
            editor.UtcDay = utc.Day;
            editor.UtcTime = utc.Time;
            editor.HasUtcSchedule = true;
        }

        configuration.TimeZoneId = timeZoneId;
    }
}
