using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class OverlayInstancePersistenceTests
{
    private const string _previousMigration = "20260730054804_v0.4.0_MomentConvergence";
    private const string _latestMigration = "20260730202307_v0.5.0_IndependentChatTools";

    [Test]
    public async Task Migration_FromV04_AddsOverlaySchemaAndFeatureWithoutLosingHosts()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.GetService<IMigrator>().MigrateAsync(_previousMigration);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, CreatedAtUtc)
                VALUES (1, 'host-id', 'host', 'Host', 0, '2026-07-30T00:00:00Z');
                """
            );
            await db.Database.MigrateAsync();
        }

        await using var migrated = await factory.CreateDbContextAsync();
        (await migrated.Hosts.Select(value => value.Login).ToArrayAsync()).ShouldBe(["host"]);
        (await migrated.Hosts.Select(value => value.EnabledFeatures).SingleAsync()).ShouldBe(
            HostFeatureFlags.All
        );
        (await migrated.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(_latestMigration);
        migrated.GetService<IMigrationsAssembly>().Migrations.Count.ShouldBe(14);
        (await migrated.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
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
        db.Hosts.Add(host);
        await db.SaveChangesAsync();

        var digest = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        db.OverlayInstances.Add(
            Overlay(host.Id, Guid.NewGuid(), digest, """{"schemaVersion":1}""")
        );
        await db.SaveChangesAsync();

        await Should.ThrowAsync<DbUpdateException>(async () =>
        {
            await using var duplicate = await factory.CreateDbContextAsync();
            duplicate.OverlayInstances.Add(
                Overlay(host.Id, Guid.NewGuid(), digest, """{"schemaVersion":1}""")
            );
            await duplicate.SaveChangesAsync();
        });
        await Should.ThrowAsync<SqliteException>(() =>
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
        await Should.ThrowAsync<SqliteException>(() =>
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
        await Should.ThrowAsync<SqliteException>(() =>
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

        var schema = await ReadSchemaAsync(db);
        schema.ShouldContain(value =>
            value.Contains(
                "CREATE UNIQUE INDEX \"IX_overlay_instances_AccessKeyDigest\"",
                StringComparison.Ordinal
            )
        );
        schema.ShouldContain(value =>
            value.Contains(
                "FOREIGN KEY (\"HostId\") REFERENCES \"hosts\" (\"Id\") ON DELETE CASCADE",
                StringComparison.Ordinal
            )
        );

        db.OverlayInstanceEvents.Add(
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
        await db.SaveChangesAsync();
        db.Hosts.Remove(host);
        await db.SaveChangesAsync();
        (await db.OverlayInstances.CountAsync()).ShouldBe(0);
        (await db.OverlayInstanceEvents.CountAsync()).ShouldBe(0);
    }

    private static OverlayInstance Overlay(
        int hostId,
        Guid publicId,
        byte[] digest,
        string configuration
    )
    {
        return new()
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
    }

    private static byte[] RandomDigest(byte seed)
    {
        return Enumerable.Range(0, 32).Select(value => (byte)(seed + value)).ToArray();
    }

    private static async Task<IReadOnlyList<string>> ReadSchemaAsync(BlokeBotDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(sql, '')
            FROM sqlite_master
            WHERE name IN (
                'overlay_instances',
                'overlay_instance_events',
                'IX_overlay_instances_AccessKeyDigest'
            )
            ORDER BY name;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }
        return values;
    }
}
