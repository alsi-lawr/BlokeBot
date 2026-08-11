using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class CompetitionMigrationTests
{
    private const string _previousMigration = "20260810154030_v0.9.0_BingoOpaqueAssignments";

    [Test]
    public async Task Upgrade_AddsOptInCompetitionSchemaWithoutChangingExistingHosts()
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
                    (1, 'host-id', 'host', 'Host', 0, 65535, 0, '2026-08-11T00:00:00Z');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        var host = await upgraded.Hosts.SingleAsync();
        (
            (host.EnabledFeatures & HostFeatureFlags.Competitions) == HostFeatureFlags.Competitions
        ).ShouldBeFalse();
        host.CompetitionsPausedAtUtc.ShouldBeNull();
        host.CompetitionsAcceptWorkAfterUtc.ShouldBeNull();
        var tableCount = await upgraded
            .Database.SqlQuery<int>(
                $"""
                SELECT COUNT(*) AS "Value"
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                      'competitions',
                      'competition_entrants',
                      'competition_entrant_members',
                      'competition_matches',
                      'competition_audits',
                      'competition_events',
                      'competition_reward_receipts'
                  )
                """
            )
            .SingleAsync();
        tableCount.ShouldBe(7);
    }
}
