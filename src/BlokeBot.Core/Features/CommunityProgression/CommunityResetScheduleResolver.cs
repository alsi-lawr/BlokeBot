using System.Globalization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CommunityProgression;

internal sealed record CommunityPeriodIdentity(
    string Key,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset NextResetUtc
);

internal static class CommunityResetScheduleResolver
{
    internal static CommunityPeriodIdentity Resolve(
        string timeZoneId,
        CommunityResetSchedule schedule,
        int scheduleRevision,
        DateTimeOffset nowUtc
    )
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime;
        var startLocal = schedule.Cadence switch
        {
            CommunityResetCadence.Daily => DailyStart(localNow, schedule.LocalTime),
            CommunityResetCadence.Weekly => WeeklyStart(
                localNow,
                schedule.LocalTime,
                schedule.Weekday ?? DayOfWeek.Monday
            ),
            _ => DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Unspecified),
        };
        var nextLocal = schedule.Cadence switch
        {
            CommunityResetCadence.Daily => startLocal.AddDays(1),
            CommunityResetCadence.Weekly => startLocal.AddDays(7),
            _ => DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Unspecified),
        };
        var startUtc = ResolveLocal(timeZone, startLocal);
        var nextUtc = ResolveLocal(timeZone, nextLocal);
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"v{scheduleRevision}:{schedule.Cadence}:{startUtc.UtcDateTime:O}"
        );
        return new(key, startUtc, nextUtc);
    }

    private static DateTime DailyStart(DateTime localNow, TimeOnly localTime)
    {
        var candidate = localNow.Date.Add(localTime.ToTimeSpan());
        return DateTime.SpecifyKind(
            candidate <= localNow ? candidate : candidate.AddDays(-1),
            DateTimeKind.Unspecified
        );
    }

    private static DateTime WeeklyStart(DateTime localNow, TimeOnly localTime, DayOfWeek weekday)
    {
        var daysBack = ((int)localNow.DayOfWeek - (int)weekday + 7) % 7;
        var candidate = localNow.Date.AddDays(-daysBack).Add(localTime.ToTimeSpan());
        if (candidate > localNow)
        {
            candidate = candidate.AddDays(-7);
        }
        return DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified);
    }

    private static DateTimeOffset ResolveLocal(TimeZoneInfo timeZone, DateTime local)
    {
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            var firstOffset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, firstOffset).ToUniversalTime();
        }

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}
