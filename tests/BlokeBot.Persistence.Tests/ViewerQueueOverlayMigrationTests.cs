using System.Data.Common;
using System.Globalization;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class ViewerQueueOverlayMigrationTests
{
    private const string _previousMigration = "20260731141254_v0.6.0_OverlayAppearance";
    private const string _migration = "20260802075446_v0.6.0_ViewerQueueOverlay";

    [Test]
    public async Task Upgrade_MakesExistingRequiredFlagsOptionalAndAllowsViewerQueueOverlays()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_previousMigration);
            _ = await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures,
                     CommandsAliasesConfigured, CreatedAtUtc)
                VALUES
                    (1, 'host-id', 'host', 'Host', 0, 511, 0, '2026-08-02T00:00:00Z');

                INSERT INTO play_queues
                    (Id, HostId, Slug, Name, ActivityName, Capacity, IsOpen, SelectionMode,
                     ShowParticipantNames, ReadinessTimeoutSeconds, HistoryRetentionDays,
                     SkipExclusionMinutes, CurrentPartyNumber, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (1, 1, 'squad', 'Community squad', 'Example game', 4, 1, 'JoinOrder',
                     1, 120, 30, 15, 0, '2026-08-02T00:00:00Z', '2026-08-02T00:00:00Z');

                INSERT INTO play_queue_fields
                    (Id, QueueId, Position, Key, Label, IsRequired, Choices)
                VALUES
                    (1, 1, 0, 'platform', 'Platform', 1, 'PC');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        (await upgraded.Database.GetAppliedMigrationsAsync()).ShouldContain(_migration);
        (await upgraded.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (
            await ReadScalarAsync(
                upgraded.Database.GetDbConnection(),
                """
                SELECT COUNT(*) FROM pragma_table_info('play_queue_fields')
                WHERE name = 'IsRequired';
                """
            )
        ).ShouldBe(0);
        (await upgraded.PlayQueueFields.SingleAsync()).Label.ShouldBe("Platform");
        _ = upgraded.OverlayInstances.Add(
            new OverlayInstance
            {
                PublicId = Guid.Parse("38a596f8-0f66-4f62-a5b1-967045c147ce"),
                HostId = 1,
                Name = "Viewer queue",
                Type = OverlayType.ViewerQueue,
                IsEnabled = true,
                ConfigurationJson =
                    """{"schemaVersion":1,"queueId":1,"currentRows":4,"nextRows":6,"appearance":{"x":160,"y":140,"width":1200,"height":800,"css":""}}""",
                AccessKeyDigest = new byte[32],
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await upgraded.SaveChangesAsync();
        (await upgraded.OverlayInstances.SingleAsync()).Type.ShouldBe(OverlayType.ViewerQueue);
        (await ReadOverlayTableSqlAsync(upgraded.Database.GetDbConnection())).ShouldContain(
            "'viewer-queue'"
        );

        _ = upgraded.OverlayInstances.Remove(await upgraded.OverlayInstances.SingleAsync());
        _ = await upgraded.SaveChangesAsync();
        await upgraded.GetService<IMigrator>().MigrateAsync(_previousMigration);
        (
            await ReadScalarAsync(
                upgraded.Database.GetDbConnection(),
                "SELECT IsRequired FROM play_queue_fields WHERE Id = 1;"
            )
        ).ShouldBe(0);
    }

    private static async Task<long> ReadScalarAsync(DbConnection connection, string sql)
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadOverlayTableSqlAsync(DbConnection connection)
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText =
            """SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'overlay_instances';""";
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
