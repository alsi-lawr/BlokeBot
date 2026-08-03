using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class AnnouncementDeliveryPolicyPersistenceTests
{
    [Test]
    public async Task RetryUntilExpiredThenSkipGraph_SavingOnce_RoundTripsRequiredPolicy()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        int announcementId;

        await using (var writeDb = await dbFactory.CreateDbContextAsync())
        {
            var hostId = await CreateHostAsync(writeDb);
            var announcement = CreateAnnouncement(hostId);
            _ = writeDb.Add(announcement);

            _ = await writeDb.SaveChangesAsync();

            announcementId = announcement.Id;
            announcement.DeliveryPolicyId.ShouldBe(announcement.DeliveryPolicy.Id);
        }

        await using var readDb = await dbFactory.CreateDbContextAsync();
        var stored = await readDb
            .CustomAnnouncements.Include(x => x.DeliveryPolicy)
            .SingleAsync(x => x.Id == announcementId);
        var policy =
            stored.DeliveryPolicy.ShouldBeOfType<RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy>();
        policy.RetryDelay.Value.ShouldBe(TimeSpan.FromSeconds(2));
        policy.OccurrenceLifetime.Value.ShouldBe(TimeSpan.FromSeconds(30));
        stored.DeliveryType.ShouldBe(CustomAnnouncementDeliveryType.ChatMessage);
        stored.AnnouncementColor.ShouldBe(TwitchAnnouncementColor.Primary);
        stored.LatestDeliveryResult.ShouldBe(CustomAnnouncementLatestDeliveryResult.None);

        var discriminator = await readDb
            .Database.SqlQueryRaw<string>(
                "SELECT PolicyType AS Value FROM custom_announcement_delivery_policies"
            )
            .SingleAsync();
        discriminator.ShouldBe(
            nameof(CustomAnnouncementDeliveryPolicyKind.RetryUntilExpiredThenSkip)
        );
    }

    [Test]
    public async Task TwitchAnnouncementDelivery_Saving_RoundTripsTypeColorAndLatestResult()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        int announcementId;

        await using (var writeDb = await dbFactory.CreateDbContextAsync())
        {
            var hostId = await CreateHostAsync(writeDb);
            var announcement = CreateAnnouncement(hostId);
            announcement.DeliveryType = CustomAnnouncementDeliveryType.TwitchAnnouncement;
            announcement.AnnouncementColor = TwitchAnnouncementColor.Purple;
            announcement.LatestDeliveryResult = CustomAnnouncementLatestDeliveryResult.Ambiguous;
            _ = writeDb.Add(announcement);
            _ = await writeDb.SaveChangesAsync();
            announcementId = announcement.Id;
        }

        await using var readDb = await dbFactory.CreateDbContextAsync();
        var stored = await readDb.CustomAnnouncements.SingleAsync(x => x.Id == announcementId);
        stored.DeliveryType.ShouldBe(CustomAnnouncementDeliveryType.TwitchAnnouncement);
        stored.AnnouncementColor.ShouldBe(TwitchAnnouncementColor.Purple);
        stored.LatestDeliveryResult.ShouldBe(CustomAnnouncementLatestDeliveryResult.Ambiguous);
    }

    [Test]
    public async Task AnnouncementWithoutPolicy_Inserting_IsRejectedByRequiredReference()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await CreateHostAsync(db);
        var entryId = await CreateMessageEntryAsync(db, hostId);
        var now = DateTime.UtcNow;

        var exception = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcements
                    (HostId, Name, Enabled, MessageLibraryEntryId, LastSentAtUtc,
                     ChatMessagesSinceLastSent, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ({hostId}, 'missing-policy', 1, {entryId}, NULL, 0, {now}, {now})
                """
            )
        );

        exception.SqliteExtendedErrorCode.ShouldBe(1299);
    }

    [Test]
    public async Task PolicySharedAcrossAnnouncements_Inserting_IsRejectedByUniqueReference()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await CreateHostAsync(db);
        var first = CreateAnnouncement(hostId);
        _ = db.Add(first);
        _ = await db.SaveChangesAsync();
        var secondEntryId = await CreateMessageEntryAsync(db, hostId);
        var now = DateTime.UtcNow;

        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcements
                    (HostId, Name, Enabled, MessageLibraryEntryId, DeliveryPolicyId,
                     LastSentAtUtc, ChatMessagesSinceLastSent, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ({hostId}, 'shared-policy', 1, {secondEntryId},
                     {first.DeliveryPolicyId}, NULL, 0, {now}, {now})
                """
            )
        );
    }

    [Test]
    public async Task PolicyFromDifferentHost_InsertingAnnouncement_IsRejectedByCompositeReference()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var firstHostId = await CreateHostAsync(db);
        var secondHostId = await CreateHostAsync(db);
        var first = CreateAnnouncement(firstHostId);
        _ = db.Add(first);
        _ = await db.SaveChangesAsync();
        var secondEntryId = await CreateMessageEntryAsync(db, secondHostId);
        var now = DateTime.UtcNow;

        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcements
                    (HostId, Name, Enabled, MessageLibraryEntryId, DeliveryPolicyId,
                     LastSentAtUtc, ChatMessagesSinceLastSent, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ({secondHostId}, 'cross-host-policy', 1, {secondEntryId},
                     {first.DeliveryPolicyId}, NULL, 0, {now}, {now})
                """
            )
        );
    }

    [Test]
    public async Task InvalidPolicyPayload_Inserting_IsRejectedByDatabaseConstraints()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await CreateHostAsync(db);

        await AssertInvalidPolicyInsertAsync(db, hostId, null, TimeSpan.FromSeconds(30).Ticks);
        await AssertInvalidPolicyInsertAsync(db, hostId, TimeSpan.FromSeconds(2).Ticks, null);
        await AssertInvalidPolicyInsertAsync(db, hostId, 0, TimeSpan.FromSeconds(30).Ticks);
        await AssertInvalidPolicyInsertAsync(
            db,
            hostId,
            TimeSpan.FromSeconds(2).Ticks,
            TimeSpan.FromSeconds(61).Ticks
        );
        await AssertInvalidPolicyInsertAsync(
            db,
            hostId,
            TimeSpan.FromSeconds(30).Ticks,
            TimeSpan.FromSeconds(30).Ticks
        );

        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcement_delivery_policies
                    (HostId, PolicyType, RetryDelayTicks, OccurrenceLifetimeTicks)
                VALUES
                    ({hostId}, 'Unsupported', {TimeSpan.FromSeconds(2).Ticks},
                     {TimeSpan.FromSeconds(30).Ticks})
                """
            )
        );
    }

    [Test]
    public async Task InconsistentOccurrenceState_Updating_IsRejectedByDatabaseConstraint()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await CreateHostAsync(db);
        var announcement = CreateAnnouncement(hostId);
        _ = db.Add(announcement);
        _ = await db.SaveChangesAsync();

        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE custom_announcements
                SET OccurrenceStatus = 'RetryScheduled', OccurrenceAttemptCount = 1
                WHERE Id = {announcement.Id}
                """
            )
        );
    }

    [Test]
    public async Task InvalidOccurrenceStateCombinations_Updating_AreRejectedByDatabaseConstraint()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await CreateHostAsync(db);
        var announcement = CreateAnnouncement(hostId);
        _ = db.Add(announcement);
        _ = await db.SaveChangesAsync();
        var dueAt = DateTime.UtcNow;
        var expiresAt = dueAt.AddSeconds(30);

        foreach (
            var status in new[]
            {
                "Accepted",
                "TerminalRejected",
                "TerminalAmbiguous",
                "TerminalUnexpected",
            }
        )
        {
            _ = await Should.ThrowAsync<SqliteException>(() =>
                db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE custom_announcements
                    SET OccurrenceStatus = {status}, OccurrenceDueAtUtc = {dueAt},
                        OccurrenceExpiresAtUtc = {expiresAt}, OccurrenceNextAttemptAtUtc = NULL,
                        OccurrenceCompletedAtUtc = {dueAt}, OccurrenceAttemptCount = 0,
                        OccurrenceMessage = NULL
                    WHERE Id = {announcement.Id}
                    """
                )
            );
        }

        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE custom_announcements
                SET OccurrenceStatus = 'TerminalMissingMessage', OccurrenceDueAtUtc = {dueAt},
                    OccurrenceExpiresAtUtc = {expiresAt}, OccurrenceNextAttemptAtUtc = NULL,
                    OccurrenceCompletedAtUtc = {dueAt}, OccurrenceAttemptCount = 1,
                    OccurrenceMessage = NULL
                WHERE Id = {announcement.Id}
                """
            )
        );
        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE custom_announcements
                SET OccurrenceStatus = 'Pending', OccurrenceDueAtUtc = {dueAt},
                    OccurrenceExpiresAtUtc = {expiresAt},
                    OccurrenceNextAttemptAtUtc = {expiresAt.AddSeconds(1)},
                    OccurrenceCompletedAtUtc = NULL, OccurrenceAttemptCount = 0,
                    OccurrenceMessage = NULL
                WHERE Id = {announcement.Id}
                """
            )
        );
        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE custom_announcements
                SET OccurrenceStatus = 'RetryScheduled', OccurrenceDueAtUtc = {dueAt},
                    OccurrenceExpiresAtUtc = {expiresAt},
                    OccurrenceNextAttemptAtUtc = {dueAt.AddSeconds(-1)},
                    OccurrenceCompletedAtUtc = NULL, OccurrenceAttemptCount = 1,
                    OccurrenceMessage = 'message'
                WHERE Id = {announcement.Id}
                """
            )
        );
    }

    [Test]
    public void TimingValues_InvalidDurations_AreRejected()
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(static () =>
            new AnnouncementRetryDelay(TimeSpan.Zero)
        );
        _ = Should.Throw<ArgumentOutOfRangeException>(static () =>
            new AnnouncementRetryDelay(TimeSpan.FromTicks(-1))
        );
        _ = Should.Throw<ArgumentOutOfRangeException>(static () =>
            new AnnouncementOccurrenceLifetime(TimeSpan.Zero)
        );
        _ = Should.Throw<ArgumentOutOfRangeException>(static () =>
            new AnnouncementOccurrenceLifetime(TimeSpan.FromSeconds(60).Add(TimeSpan.FromTicks(1)))
        );
        new AnnouncementOccurrenceLifetime(TimeSpan.FromSeconds(60)).Value.ShouldBe(
            TimeSpan.FromSeconds(60)
        );
    }

    private static async Task AssertInvalidPolicyInsertAsync(
        BlokeBotDbContext db,
        int hostId,
        long? retryDelayTicks,
        long? occurrenceLifetimeTicks
    ) =>
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcement_delivery_policies
                    (HostId, PolicyType, RetryDelayTicks, OccurrenceLifetimeTicks)
                VALUES
                    ({hostId}, 'RetryUntilExpiredThenSkip', {retryDelayTicks},
                     {occurrenceLifetimeTicks})
                """
            )
        );

    private static CustomAnnouncement CreateAnnouncement(int hostId)
    {
        var now = DateTime.UtcNow;
        return new CustomAnnouncement
        {
            HostId = hostId,
            Name = $"announcement-{Guid.NewGuid():N}",
            MessageLibraryEntry = new CustomMessageLibraryEntry
            {
                HostId = hostId,
                Name = $"message-{Guid.NewGuid():N}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
            Schedule = new IntervalCustomAnnouncementSchedule
            {
                HostId = hostId,
                IntervalMinutes = 30,
            },
            DeliveryPolicy = Policy(hostId),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy Policy(int hostId) =>
        new()
        {
            HostId = hostId,
            RetryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(2)),
            OccurrenceLifetime = new AnnouncementOccurrenceLifetime(TimeSpan.FromSeconds(30)),
        };

    private static async Task<int> CreateMessageEntryAsync(BlokeBotDbContext db, int hostId)
    {
        var now = DateTime.UtcNow;
        var entry = new CustomMessageLibraryEntry
        {
            HostId = hostId,
            Name = $"message-{Guid.NewGuid():N}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _ = db.Add(entry);
        _ = await db.SaveChangesAsync();
        return entry.Id;
    }

    private static async Task<int> CreateHostAsync(BlokeBotDbContext db)
    {
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = $"host-{Guid.NewGuid():N}",
            DisplayName = "Host",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
