using BlokeBot.Announcements;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using TUnit.Core;

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
            writeDb.Add(announcement);

            await writeDb.SaveChangesAsync();

            announcementId = announcement.Id;
            announcement.DeliveryPolicyId.ShouldBe(announcement.DeliveryPolicy.Id);
        }

        await using var readDb = await dbFactory.CreateDbContextAsync();
        var stored = await readDb.CustomAnnouncements
            .Include(x => x.DeliveryPolicy)
            .SingleAsync(x => x.Id == announcementId);
        var policy = stored.DeliveryPolicy.ShouldBeOfType<
            RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
        >();
        policy.RetryDelay.Value.ShouldBe(TimeSpan.FromSeconds(2));
        policy.OccurrenceLifetime.Value.ShouldBe(TimeSpan.FromSeconds(30));

        var discriminator = await readDb.Database.SqlQueryRaw<string>(
            "SELECT PolicyType AS Value FROM custom_announcement_delivery_policies"
        ).SingleAsync();
        discriminator.ShouldBe(nameof(CustomAnnouncementDeliveryPolicyKind.RetryUntilExpiredThenSkip));
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
        db.Add(first);
        await db.SaveChangesAsync();
        var secondEntryId = await CreateMessageEntryAsync(db, hostId);
        var now = DateTime.UtcNow;

        await Should.ThrowAsync<SqliteException>(() =>
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
        db.Add(first);
        await db.SaveChangesAsync();
        var secondEntryId = await CreateMessageEntryAsync(db, secondHostId);
        var now = DateTime.UtcNow;

        await Should.ThrowAsync<SqliteException>(() =>
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

        await Should.ThrowAsync<SqliteException>(() =>
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
    public async Task PolicyModel_IsOneRequiredHostScopedTypedTphHierarchy()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var model = db.GetService<IDesignTimeModel>().Model;
        var baseType = model.FindEntityType(typeof(CustomAnnouncementDeliveryPolicy));
        baseType.ShouldNotBeNull();
        var leafType = baseType.GetDerivedTypes().ShouldHaveSingleItem();
        leafType.ClrType.ShouldBe(
            typeof(RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy)
        );
        leafType.GetTableName().ShouldBe(baseType.GetTableName());

        var discriminator = baseType.FindDiscriminatorProperty();
        discriminator.ShouldNotBeNull();
        discriminator.ClrType.ShouldBe(typeof(CustomAnnouncementDeliveryPolicyKind));
        discriminator.IsNullable.ShouldBeFalse();
        baseType.FindPrimaryKey()!.Properties.Select(x => x.Name).ShouldBe(
            [nameof(CustomAnnouncementDeliveryPolicy.Id)]
        );
        baseType.FindKey(
                [
                    baseType.FindProperty(nameof(CustomAnnouncementDeliveryPolicy.HostId))!,
                    baseType.FindProperty(nameof(CustomAnnouncementDeliveryPolicy.Id))!,
                ]
            )
            .ShouldNotBeNull();

        var announcementType = model.FindEntityType(typeof(CustomAnnouncement));
        announcementType.ShouldNotBeNull();
        var relationship = announcementType
            .GetForeignKeys()
            .Single(x => x.PrincipalEntityType == baseType);
        relationship.Properties.Select(x => x.Name).ShouldBe(
            [nameof(CustomAnnouncement.HostId), nameof(CustomAnnouncement.DeliveryPolicyId)]
        );
        relationship.IsUnique.ShouldBeTrue();
        relationship.IsRequired.ShouldBeTrue();
        relationship.DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);

        baseType.GetCheckConstraints().Select(x => x.Name).ShouldBe(
            [
                "CK_custom_announcement_delivery_policies_Payload",
                "CK_custom_announcement_delivery_policies_PolicyType",
            ],
            ignoreOrder: true
        );
    }

    [Test]
    public async Task InconsistentOccurrenceState_Updating_IsRejectedByDatabaseConstraint()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await CreateHostAsync(db);
        var announcement = CreateAnnouncement(hostId);
        db.Add(announcement);
        await db.SaveChangesAsync();

        await Should.ThrowAsync<SqliteException>(() =>
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
    public void TimingValues_InvalidDurations_AreRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new AnnouncementRetryDelay(TimeSpan.Zero)
        );
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new AnnouncementRetryDelay(TimeSpan.FromTicks(-1))
        );
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new AnnouncementOccurrenceLifetime(TimeSpan.Zero)
        );
        Should.Throw<ArgumentOutOfRangeException>(() =>
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
            OccurrenceLifetime = new AnnouncementOccurrenceLifetime(
                TimeSpan.FromSeconds(30)
            ),
        };

    private static async Task<int> CreateMessageEntryAsync(
        BlokeBotDbContext db,
        int hostId
    )
    {
        var now = DateTime.UtcNow;
        var entry = new CustomMessageLibraryEntry
        {
            HostId = hostId,
            Name = $"message-{Guid.NewGuid():N}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private static async Task<int> CreateHostAsync(BlokeBotDbContext db)
    {
        var host = new BotHost
        {
            Login = $"host-{Guid.NewGuid():N}",
            DisplayName = "Host",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
