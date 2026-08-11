using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class OverlayInstancePersistenceTests
{
    private const string _previousMigration = "20260730054804_v0.4.0_MomentConvergence";
    private const string _v090Migration = "20260810154030_v0.9.0_BingoOpaqueAssignments";
    private const HostFeatureFlags _preAutomationsEnabledFeatures =
        HostFeatureFlags.Guessing
        | HostFeatureFlags.Points
        | HostFeatureFlags.CustomCommands
        | HostFeatureFlags.Shoutouts
        | HostFeatureFlags.Overlays
        | HostFeatureFlags.RequestBoards
        | HostFeatureFlags.PlayWithViewers
        | HostFeatureFlags.Moments
        | HostFeatureFlags.Polls
        | HostFeatureFlags.ClipsAndMarkers
        | HostFeatureFlags.RewardsAndRedemptions
        | HostFeatureFlags.Predictions;

    [Test]
    public async Task Migration_FromV04_AddsOverlaySchemaAndFeatureWithoutLosingHosts()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.GetService<IMigrator>().MigrateAsync(_previousMigration);
            _ = await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, CreatedAtUtc)
                VALUES (1, 'host-id', 'host', 'Host', 0, '2026-07-30T00:00:00Z');
                """
            );
            await db.Database.MigrateAsync();
        }

        await using var migrated = await factory.CreateDbContextAsync();
        (await migrated.Hosts.Select(static value => value.Login).ToArrayAsync()).ShouldBe([
            "host",
        ]);
        (await migrated.Hosts.Select(static value => value.EnabledFeatures).SingleAsync()).ShouldBe(
            _preAutomationsEnabledFeatures
        );
        (await migrated.OverlayInstances.CountAsync()).ShouldBe(0);
        (await migrated.OverlayInstanceEvents.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task OverlayConstraintsIndexesAndHostCascades_AreDatabaseEnforced()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await factory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            TwitchUserId = "host-id",
            Login = "host",
            DisplayName = "Host",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();

        var digest = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        _ = db.OverlayInstances.Add(
            Overlay(host.Id, Guid.NewGuid(), digest, """{"schemaVersion":1}""")
        );
        _ = await db.SaveChangesAsync();

        _ = await Should.ThrowAsync<DbUpdateException>(async () =>
        {
            await using var duplicate = await factory.CreateDbContextAsync();
            _ = duplicate.OverlayInstances.Add(
                Overlay(host.Id, Guid.NewGuid(), digest, """{"schemaVersion":1}""")
            );
            _ = await duplicate.SaveChangesAsync();
        });
        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO overlay_instances
                    (PublicId, HostId, Name, Type, IsEnabled, ConfigurationJson,
                     AccessKeyDigest, KeyVersion, Revision, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ({Guid.NewGuid().ToString()}, {host.Id}, {"bad"}, {"unknown"}, {true},
                     {"""{"schemaVersion":1}"""}, {RandomDigest(1)}, 1, 1,
                     {DateTime.UtcNow}, {DateTime.UtcNow});
                """
            )
        );
        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO overlay_instances
                    (PublicId, HostId, Name, Type, IsEnabled, ConfigurationJson,
                     AccessKeyDigest, KeyVersion, Revision, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ({Guid.NewGuid().ToString()}, {host.Id}, {"bad"}, {"empty"}, {true},
                     {"not-json"}, {RandomDigest(2)}, 1, 1,
                     {DateTime.UtcNow}, {DateTime.UtcNow});
                """
            )
        );
        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO overlay_instances
                    (PublicId, HostId, Name, Type, IsEnabled, ConfigurationJson,
                     AccessKeyDigest, KeyVersion, Revision, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ({Guid.NewGuid().ToString()}, {host.Id + 1000}, {"bad"}, {"empty"}, {true},
                     {"""{"schemaVersion":1}"""}, {RandomDigest(3)}, 1, 1,
                     {DateTime.UtcNow}, {DateTime.UtcNow});
                """
            )
        );

        _ = db.OverlayInstanceEvents.Add(
            new OverlayInstanceDomainEvent
            {
                HostId = host.Id,
                OverlayPublicId = Guid.NewGuid(),
                SchemaVersion = 1,
                Kind = OverlayInstanceEventKind.Deleted,
                ActorUserId = "actor-id",
                ActorLogin = "actor",
                OverlayRevision = 2,
                KeyVersion = 1,
                OccurredAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
        _ = db.Hosts.Remove(host);
        _ = await db.SaveChangesAsync();
        (await db.OverlayInstances.CountAsync()).ShouldBe(0);
        (await db.OverlayInstanceEvents.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task CommunityProgressOverlayMigration_PreservesSourcesAndAllowsOnlyNewTypedKinds()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.GetService<IMigrator>().MigrateAsync(_v090Migration);
            _ = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO hosts
                    (TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures,
                     CommandsAliasesConfigured, CreatedAtUtc)
                VALUES
                    ({"host-id"}, {"host"}, {"Host"}, {0}, {(ulong)
                    HostFeatureFlags.All}, {false}, {DateTime.UtcNow});
                """
            );
            var hostId = await db
                .Hosts.AsNoTracking()
                .Where(value => value.Login == "host")
                .Select(value => value.Id)
                .SingleAsync();
            _ = db.OverlayInstances.Add(
                Overlay(hostId, Guid.NewGuid(), RandomDigest(30), """{"schemaVersion":1}""")
            );
            _ = await db.SaveChangesAsync();
            await db.Database.MigrateAsync();

            var goal = Overlay(
                hostId,
                Guid.NewGuid(),
                RandomDigest(60),
                """{"schemaVersion":1,"selectedItemId":null,"rotationSeconds":20,"recentContributorCount":0,"appearance":{"x":1160,"y":80,"width":680,"height":300,"css":""}}"""
            );
            goal.Type = OverlayType.CommunityGoal;
            _ = db.OverlayInstances.Add(goal);
            var bounty = Overlay(
                hostId,
                Guid.NewGuid(),
                RandomDigest(90),
                """{"schemaVersion":1,"selectedItemId":null,"rotationSeconds":20,"recentContributorCount":3,"appearance":{"x":1160,"y":80,"width":680,"height":340,"css":""}}"""
            );
            bounty.Type = OverlayType.ViewerFundedBounty;
            _ = db.OverlayInstances.Add(bounty);
            _ = await db.SaveChangesAsync();
        }

        await using var migrated = await factory.CreateDbContextAsync();
        (
            await migrated
                .OverlayInstances.OrderBy(value => value.Id)
                .Select(value => value.Type)
                .ToArrayAsync()
        ).ShouldBe([OverlayType.Empty, OverlayType.CommunityGoal, OverlayType.ViewerFundedBounty]);
    }

    private static OverlayInstance Overlay(
        int hostId,
        Guid publicId,
        byte[] digest,
        string configuration
    ) =>
        new()
        {
            PublicId = publicId,
            HostId = hostId,
            Name = "Overlay",
            Type = OverlayType.Empty,
            IsEnabled = true,
            ConfigurationJson = configuration,
            AccessKeyDigest = digest,
            KeyVersion = 1,
            Revision = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static byte[] RandomDigest(byte seed) =>
        Enumerable.Range(0, 32).Select(value => (byte)(seed + value)).ToArray();
}
