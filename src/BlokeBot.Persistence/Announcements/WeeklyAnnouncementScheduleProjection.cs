namespace BlokeBot.Announcements;

public readonly record struct WeeklyAnnouncementSchedule(DayOfWeek Day, TimeOnly Time);

public static class WeeklyAnnouncementScheduleProjection
{
    public static WeeklyAnnouncementSchedule ToLocal(
        WeeklyAnnouncementSchedule utcSchedule,
        TimeZoneInfo timeZone,
        DateTimeOffset referenceUtc
    )
    {
        var occurrenceUtc = NextUtcOccurrence(utcSchedule, referenceUtc);
        var local = TimeZoneInfo.ConvertTime(occurrenceUtc, timeZone);
        return new(local.DayOfWeek, TimeOnly.FromDateTime(local.DateTime));
    }

    public static WeeklyAnnouncementSchedule ToUtc(
        WeeklyAnnouncementSchedule localSchedule,
        TimeZoneInfo timeZone,
        DateTimeOffset referenceUtc
    )
    {
        var localReference = TimeZoneInfo.ConvertTime(referenceUtc, timeZone).DateTime;
        var occurrenceLocal = NextLocalOccurrence(localSchedule, timeZone, localReference);
        var occurrenceUtc = TimeZoneInfo.ConvertTimeToUtc(occurrenceLocal, timeZone);
        return new(occurrenceUtc.DayOfWeek, TimeOnly.FromDateTime(occurrenceUtc));
    }

    public static DateTimeOffset NextUtcOccurrence(
        WeeklyAnnouncementSchedule schedule,
        DateTimeOffset referenceUtc
    )
    {
        var utcReference = referenceUtc.UtcDateTime;
        var date = DateOnly
            .FromDateTime(utcReference)
            .AddDays(DaysUntil(utcReference.DayOfWeek, schedule.Day));
        var occurrence = DateTime.SpecifyKind(date.ToDateTime(schedule.Time), DateTimeKind.Utc);
        if (occurrence < utcReference)
        {
            occurrence = occurrence.AddDays(7);
        }

        return new(occurrence, TimeSpan.Zero);
    }

    private static DateTime NextLocalOccurrence(
        WeeklyAnnouncementSchedule schedule,
        TimeZoneInfo timeZone,
        DateTime localReference
    )
    {
        var date = DateOnly
            .FromDateTime(localReference)
            .AddDays(DaysUntil(localReference.DayOfWeek, schedule.Day));
        var occurrence = date.ToDateTime(schedule.Time, DateTimeKind.Unspecified);
        if (occurrence < localReference)
        {
            occurrence = occurrence.AddDays(7);
        }
        while (timeZone.IsInvalidTime(occurrence))
        {
            occurrence = occurrence.AddDays(7);
        }

        return occurrence;
    }

    private static int DaysUntil(DayOfWeek current, DayOfWeek target) =>
        ((int)target - (int)current + 7) % 7;
}
