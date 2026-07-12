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
    public async Task RetryUntilExpiredThenSkipPolicy_RoundTripsTypedTimingValues()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        int announcementId;

        await using (var writeDb = await dbFactory.CreateDbContextAsync())
        {
            var announcement = await CreateAnnouncementAsync(writeDb);
            announcement.DeliveryPolicy = Policy(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
            await writeDb.SaveChangesAsync();
            announcementId = announcement.Id;
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
    public async Task InvalidPolicyPayload_Inserting_IsRejectedByDatabaseConstraints()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var announcement = await CreateAnnouncementAsync(db);

        await AssertInvalidInsertAsync(db, announcement, null, TimeSpan.FromSeconds(30).Ticks);
        await AssertInvalidInsertAsync(db, announcement, TimeSpan.FromSeconds(2).Ticks, null);
        await AssertInvalidInsertAsync(db, announcement, 0, TimeSpan.FromSeconds(30).Ticks);
        await AssertInvalidInsertAsync(
            db,
            announcement,
            TimeSpan.FromSeconds(2).Ticks,
            TimeSpan.FromSeconds(61).Ticks
        );
        await AssertInvalidInsertAsync(
            db,
            announcement,
            TimeSpan.FromSeconds(30).Ticks,
            TimeSpan.FromSeconds(30).Ticks
        );

        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcement_delivery_policies
                    (CustomAnnouncementId, HostId, PolicyType, RetryDelayTicks,
                     OccurrenceLifetimeTicks)
                VALUES
                    ({announcement.Id}, {announcement.HostId}, 'Unsupported',
                     {TimeSpan.FromSeconds(2).Ticks}, {TimeSpan.FromSeconds(30).Ticks})
                """
            )
        );
    }

    [Test]
    public async Task SecondPolicyForAnnouncement_Inserting_IsRejectedByPrimaryKey()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var announcement = await CreateAnnouncementAsync(db);
        announcement.DeliveryPolicy = Policy(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
        await db.SaveChangesAsync();

        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcement_delivery_policies
                    (CustomAnnouncementId, HostId, PolicyType, RetryDelayTicks,
                     OccurrenceLifetimeTicks)
                VALUES
                    ({announcement.Id}, {announcement.HostId},
                     'RetryUntilExpiredThenSkip', {TimeSpan.FromSeconds(3).Ticks},
                     {TimeSpan.FromSeconds(30).Ticks})
                """
            )
        );
    }

    [Test]
    public async Task PolicyForDifferentHost_Inserting_IsRejectedByRelationship()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var announcement = await CreateAnnouncementAsync(db);
        var otherHostId = await CreateHostAsync(db);

        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcement_delivery_policies
                    (CustomAnnouncementId, HostId, PolicyType, RetryDelayTicks,
                     OccurrenceLifetimeTicks)
                VALUES
                    ({announcement.Id}, {otherHostId}, 'RetryUntilExpiredThenSkip',
                     {TimeSpan.FromSeconds(2).Ticks}, {TimeSpan.FromSeconds(30).Ticks})
                """
            )
        );
    }

    [Test]
    public async Task PolicyModel_IsOneRequiredTypedTphHierarchy()
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

        var primaryKey = baseType.FindPrimaryKey();
        primaryKey.ShouldNotBeNull();
        primaryKey.Properties.Select(x => x.Name).ShouldBe([nameof(CustomAnnouncementDeliveryPolicy.CustomAnnouncementId)]);

        var relationship = baseType.GetForeignKeys().ShouldHaveSingleItem();
        relationship.PrincipalEntityType.ClrType.ShouldBe(typeof(CustomAnnouncement));
        relationship.IsUnique.ShouldBeTrue();
        relationship.IsRequired.ShouldBeTrue();
        relationship.IsRequiredDependent.ShouldBeTrue();
        relationship.DeleteBehavior.ShouldBe(DeleteBehavior.Cascade);

        var constraints = baseType.GetCheckConstraints().Select(x => x.Name).ToArray();
        constraints.ShouldBe(
            [
                "CK_custom_announcement_delivery_policies_Payload",
                "CK_custom_announcement_delivery_policies_PolicyType",
            ],
            ignoreOrder: true
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

    private static RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy Policy(
        TimeSpan retryDelay,
        TimeSpan occurrenceLifetime
    ) =>
        new()
        {
            RetryDelay = new AnnouncementRetryDelay(retryDelay),
            OccurrenceLifetime = new AnnouncementOccurrenceLifetime(occurrenceLifetime),
        };

    private static async Task AssertInvalidInsertAsync(
        BlokeBotDbContext db,
        CustomAnnouncement announcement,
        long? retryDelayTicks,
        long? occurrenceLifetimeTicks
    ) =>
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_announcement_delivery_policies
                    (CustomAnnouncementId, HostId, PolicyType, RetryDelayTicks,
                     OccurrenceLifetimeTicks)
                VALUES
                    ({announcement.Id}, {announcement.HostId},
                     'RetryUntilExpiredThenSkip', {retryDelayTicks},
                     {occurrenceLifetimeTicks})
                """
            )
        );

    private static async Task<CustomAnnouncement> CreateAnnouncementAsync(
        BlokeBotDbContext db
    )
    {
        var hostId = await CreateHostAsync(db);
        var now = DateTime.UtcNow;
        var entry = new CustomMessageLibraryEntry
        {
            HostId = hostId,
            Name = $"message-{Guid.NewGuid():N}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var announcement = new CustomAnnouncement
        {
            HostId = hostId,
            Name = $"announcement-{Guid.NewGuid():N}",
            MessageLibraryEntry = entry,
            Schedule = new IntervalCustomAnnouncementSchedule
            {
                HostId = hostId,
                IntervalMinutes = 30,
            },
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Add(announcement);
        await db.SaveChangesAsync();
        return announcement;
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
