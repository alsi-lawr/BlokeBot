using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class BountyMigrationTests
{
    private const string _previousMigration = "20260807121536_v0.7.0_AutomationEventReceipts";

    [Test]
    public async Task Upgrade_PreservesExistingPointLedgerAndAddsBountySchema()
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
                    (1, 'host-id', 'host', 'Host', 0, 0, 0, '2026-08-09T00:00:00Z');
                """
            );
            _ = await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO point_ledger_entries
                    (Id, HostId, CreatedAtUtc, Kind, Login, Delta, BalanceAfter, Note)
                VALUES
                    (1, 1, '2026-08-09T00:00:00Z', 'Add', 'viewer', '10', '10', 'seed');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        var ledger = await upgraded.PointLedgerEntries.SingleAsync();
        ledger.Login.ShouldBe("viewer");
        ledger.BountyPledgeId.ShouldBeNull();
        ledger.BountyRewardId.ShouldBeNull();
        (await upgraded.Bounties.CountAsync()).ShouldBe(0);
        var existingHost = await upgraded.Hosts.SingleAsync();
        (existingHost.EnabledFeatures & HostFeatureFlags.Bounties).ShouldBe(HostFeatureFlags.None);
        existingHost.BountiesPausedAtUtc.ShouldBeNull();
        var tableCount = await upgraded
            .Database.SqlQuery<int>(
                $"""
                SELECT COUNT(*) AS "Value"
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                      'bounties',
                      'bounty_pledges',
                      'bounty_contributor_rewards',
                      'bounty_moderation_audit',
                      'bounty_events'
                  )
                """
            )
            .SingleAsync();
        tableCount.ShouldBe(5);
    }
}
