using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class OverlayAppearanceMigrationTests
{
    private const string _previous = "20260731110140_v0.6.0_EventFeedOverlay";

    [Test]
    public async Task Upgrade_AddsEquivalentAppearanceToEveryExistingVisualOverlay()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_previous);
            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures,
                     CommandsAliasesConfigured, CreatedAtUtc)
                VALUES
                    (1, 'host-id', 'host', 'Host', 0, 511, 0, '2026-07-31T00:00:00Z');

                INSERT INTO overlay_instances
                    (PublicId, HostId, Name, Type, IsEnabled, ConfigurationJson,
                     AccessKeyDigest, KeyVersion, Revision, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ('00000000-0000-0000-0000-000000000001', 1, 'Guessing', 'guessing', 1,
                     '{{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":8}}',
                     randomblob(32), 1, 1, '2026-07-31T00:00:00Z', '2026-07-31T00:00:00Z'),
                    ('00000000-0000-0000-0000-000000000002', 1, 'Giveaway', 'giveaway', 1,
                     '{{"schemaVersion":1,"title":"Giveaway","showEntrantCount":true,"showCountdown":true,"showJoinCommand":true}}',
                     randomblob(32), 1, 1, '2026-07-31T00:00:00Z', '2026-07-31T00:00:00Z'),
                    ('00000000-0000-0000-0000-000000000003', 1, 'Empty', 'empty', 1,
                     '{{"schemaVersion":1}}',
                     randomblob(32), 1, 1, '2026-07-31T00:00:00Z', '2026-07-31T00:00:00Z');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        var json = await upgraded
            .OverlayInstances.OrderBy(value => value.Name)
            .Select(value => value.ConfigurationJson)
            .ToArrayAsync();

        json[0].ShouldNotContain("\"appearance\"");
        json[1]
            .ShouldContain(
                "\"appearance\":{\"x\":160,\"y\":690,\"width\":1600,\"height\":270,\"css\":\"\"}"
            );
        json[2]
            .ShouldContain(
                "\"appearance\":{\"x\":160,\"y\":690,\"width\":1600,\"height\":270,\"css\":\"\"}"
            );
    }
}
