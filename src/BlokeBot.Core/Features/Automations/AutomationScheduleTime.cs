namespace BlokeBot.Core.Features.Automations;

internal static class AutomationScheduleTime
{
    internal static DateTimeOffset? ResolveLocal(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            return null;
        }
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    internal static DateTimeOffset? Next(
        ScheduledTimeSourceConfiguration schedule,
        DateTimeOffset after
    )
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(schedule.Zone, out var zone))
        {
            return null;
        }
        var anchor = ResolveLocal(schedule.LocalTime, zone);
        switch (schedule.Kind)
        {
            case ScheduledTimeKind.Once:
                return anchor > after ? anchor : null;
            case ScheduledTimeKind.Interval:
                if (schedule.Interval <= TimeSpan.Zero)
                {
                    return null;
                }
                var localAnchor = schedule.LocalTime;
                while (anchor is null)
                {
                    if ((DateTime.MaxValue - localAnchor).Ticks < schedule.Interval.Ticks)
                    {
                        return null;
                    }
                    localAnchor = localAnchor.Add(schedule.Interval);
                    anchor = ResolveLocal(localAnchor, zone);
                }
                if (anchor > after)
                {
                    return anchor;
                }
                var count = ((after - anchor.Value).Ticks / schedule.Interval.Ticks) + 1;
                return
                    count
                    <= (DateTimeOffset.MaxValue - anchor.Value).Ticks / schedule.Interval.Ticks
                    ? anchor.Value.AddTicks(count * schedule.Interval.Ticks)
                    : null;
            case ScheduledTimeKind.Weekly:
                var date = TimeZoneInfo.ConvertTime(after, zone).Date;
                for (var days = 0; days < 15; days++)
                {
                    var local = date.AddDays(days) + schedule.LocalTime.TimeOfDay;
                    if (local.DayOfWeek != schedule.Day)
                    {
                        continue;
                    }
                    var instant = ResolveLocal(local, zone);
                    if (instant > after)
                    {
                        return instant;
                    }
                }
                return null;
            default:
                return null;
        }
    }
}
