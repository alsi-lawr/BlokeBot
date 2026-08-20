using System.Data.Common;
using System.Globalization;
using BlokeBot.Announcements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlokeBot.Persistence;

public sealed class WeeklyAnnouncementMigrationInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Register(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        Register(connection);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    internal static void Register(DbConnection connection)
    {
        if (connection is not SqliteConnection sqlite)
        {
            return;
        }

        sqlite.CreateFunction<int, string, string, string, int>(
            "blokebot_weekly_utc_day",
            static (day, time, timeZoneId, referenceUtc) =>
                (int)Convert(day, time, timeZoneId, referenceUtc).Day
        );
        sqlite.CreateFunction<int, string, string, string, string>(
            "blokebot_weekly_utc_time",
            static (day, time, timeZoneId, referenceUtc) =>
                Convert(day, time, timeZoneId, referenceUtc)
                    .Time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)
        );
    }

    private static WeeklyAnnouncementSchedule Convert(
        int day,
        string time,
        string timeZoneId,
        string referenceUtc
    ) =>
        WeeklyAnnouncementScheduleProjection.ToUtc(
            new((DayOfWeek)day, TimeOnly.Parse(time, CultureInfo.InvariantCulture)),
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId),
            DateTimeOffset.Parse(
                referenceUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
            )
        );
}
