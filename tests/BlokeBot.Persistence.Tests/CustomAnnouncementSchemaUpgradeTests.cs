using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class CustomAnnouncementSchemaUpgradeTests
{
    [Test]
    public async Task LegacyAnnouncementTable_Initializing_AddsDefaultsWithoutChangingScheduleOrRetryState()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE custom_announcements (
                    Id INTEGER PRIMARY KEY,
                    HostId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    Enabled INTEGER NOT NULL,
                    MessageLibraryEntryId INTEGER NOT NULL,
                    DeliveryPolicyId INTEGER NOT NULL,
                    LastSentAtUtc TEXT NULL,
                    LastOccurrenceAtUtc TEXT NULL,
                    OccurrenceStatus TEXT NOT NULL,
                    OccurrenceDueAtUtc TEXT NULL,
                    OccurrenceExpiresAtUtc TEXT NULL,
                    OccurrenceNextAttemptAtUtc TEXT NULL,
                    OccurrenceCompletedAtUtc TEXT NULL,
                    OccurrenceAttemptCount INTEGER NOT NULL,
                    OccurrenceMessage TEXT NULL,
                    ChatMessagesSinceLastSent INTEGER NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                """
            );
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO custom_announcements
                    (Id, HostId, Name, Enabled, MessageLibraryEntryId, DeliveryPolicyId,
                     LastSentAtUtc, LastOccurrenceAtUtc, OccurrenceStatus, OccurrenceDueAtUtc,
                     OccurrenceExpiresAtUtc, OccurrenceNextAttemptAtUtc, OccurrenceCompletedAtUtc,
                     OccurrenceAttemptCount, OccurrenceMessage, ChatMessagesSinceLastSent,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (7, 3, 'legacy', 1, 11, 13, '2026-07-18T10:00:00Z',
                     '2026-07-18T10:30:00Z', 'RetryScheduled', '2026-07-18T10:30:00Z',
                     '2026-07-18T10:31:00Z', '2026-07-18T10:30:05Z', NULL, 2,
                     'existing retry message', 9, '2026-07-18T09:00:00Z', '2026-07-18T10:30:00Z');
                """
            );
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE custom_announcement_schedules (
                    CustomAnnouncementId INTEGER PRIMARY KEY,
                    HostId INTEGER NOT NULL,
                    ScheduleType TEXT NOT NULL,
                    IntervalMinutes INTEGER NOT NULL,
                    RequiredChatMessages INTEGER NULL,
                    WeeklyDay INTEGER NULL,
                    WeeklyTime TEXT NULL
                );
                """
            );
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO custom_announcement_schedules
                    (CustomAnnouncementId, HostId, ScheduleType, IntervalMinutes,
                     RequiredChatMessages, WeeklyDay, WeeklyTime)
                VALUES (7, 3, 'IntervalAfterChat', 45, 9, NULL, NULL);
                """
            );
        }

        var initializer = new BlokeBotDatabaseInitializer(dbFactory);
        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        await using var verify = await dbFactory.CreateDbContextAsync();
        var connection = (SqliteConnection)verify.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.DeliveryType, a.AnnouncementColor, a.LatestDeliveryResult,
                   a.OccurrenceStatus, a.OccurrenceNextAttemptAtUtc, a.OccurrenceAttemptCount,
                   a.OccurrenceMessage, a.ChatMessagesSinceLastSent, s.ScheduleType,
                   s.IntervalMinutes, s.RequiredChatMessages
            FROM custom_announcements AS a
            INNER JOIN custom_announcement_schedules AS s
                ON s.CustomAnnouncementId = a.Id
            WHERE a.Id = 7;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("ChatMessage");
        reader.GetString(1).ShouldBe("Primary");
        reader.GetString(2).ShouldBe("None");
        reader.GetString(3).ShouldBe("RetryScheduled");
        reader.GetString(4).ShouldBe("2026-07-18T10:30:05Z");
        reader.GetInt32(5).ShouldBe(2);
        reader.GetString(6).ShouldBe("existing retry message");
        reader.GetInt32(7).ShouldBe(9);
        reader.GetString(8).ShouldBe("IntervalAfterChat");
        reader.GetInt32(9).ShouldBe(45);
        reader.GetInt32(10).ShouldBe(9);
    }
}
