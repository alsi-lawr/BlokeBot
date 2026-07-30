using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class ViewerCommandCatalogMigrationTests
{
    private const string _previousMigration = "20260730141846_v0.5.0_OverlayFeatureSwitch";
    private const string _catalogMigration = "20260730162013_v0.5.0_ViewerCommandCatalog";
    private const string _independentChatTools = "20260730202307_v0.5.0_IndependentChatTools";

    [Test]
    public async Task Migration_BackfillsCanonicalOrderDefaultCatalogAndOnlyUntouchedPointsJoin()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_previousMigration);
            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts (Id, Login, DisplayName, BotRuntimeState, CreatedAtUtc)
                VALUES
                    (1, 'default', 'Default', 0, '2026-07-30T00:00:00Z'),
                    (2, 'customized', 'Customized', 0, '2026-07-30T00:00:00Z'),
                    (3, 'conflict', 'Conflict', 0, '2026-07-30T00:00:00Z');

                INSERT INTO command_aliases (HostId, GuessRoundProfileId, Kind, Alias)
                VALUES
                    (1, NULL, 'Points', 'points'),
                    (1, NULL, 'GivePoints', 'givepoints'),
                    (1, NULL, 'AddPoints', 'addpoints'),
                    (1, NULL, 'RemovePoints', 'removepoints'),
                    (1, NULL, 'Gamble', 'gamble'),
                    (1, NULL, 'Giveaway', 'giveaway'),
                    (1, NULL, 'Join', 'join'),
                    (1, NULL, 'EndGiveaway', 'endgiveaway'),
                    (1, NULL, 'CancelGiveaway', 'cancelgiveaway'),
                    (2, NULL, 'Join', 'join'),
                    (3, NULL, 'Points', 'commands');

                INSERT INTO custom_commands
                    (Id, HostId, Name, Enabled, ModeratorOnly, CooldownSeconds, CooldownScope,
                     InvocationLimit, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (1, 1, 'Legacy', 1, 0, 0, 'Global', 'Unlimited',
                     '2026-07-30T00:00:00Z', '2026-07-30T00:00:00Z');

                INSERT INTO custom_command_aliases (HostId, CustomCommandId, Alias)
                VALUES (1, 1, 'zeta'), (1, 1, 'Alpha'), (1, 1, 'beta');
                """
            );
            await before.GetService<IMigrator>().MigrateAsync(_catalogMigration);
        }

        await using var migrated = await factory.CreateDbContextAsync();
        (await migrated.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(_catalogMigration);
        migrated.GetService<IMigrationsAssembly>().Migrations.Count.ShouldBe(14);
        (
            await migrated
                .CustomCommandAliases.OrderBy(value => value.SortOrder)
                .Select(value => value.Alias)
                .ToArrayAsync()
        ).ShouldBe(["Alpha", "beta", "zeta"]);
        (
            await migrated
                .CommandAliases.Where(value =>
                    value.HostId == 1 && value.Kind == AppCommandKind.Join
                )
                .Select(value => value.Alias)
                .SingleAsync()
        ).ShouldBe("enter");
        (
            await migrated
                .CommandAliases.Where(value =>
                    value.HostId == 2 && value.Kind == AppCommandKind.Join
                )
                .Select(value => value.Alias)
                .SingleAsync()
        ).ShouldBe("join");
        (
            await migrated
                .CommandAliases.Where(value => value.Kind == AppCommandKind.Commands)
                .OrderBy(value => value.HostId)
                .Select(value => value.HostId)
                .ToArrayAsync()
        ).ShouldBe([1, 2]);
        var conflict = await migrated.Hosts.SingleAsync(value => value.Id == 3);
        conflict.CommandsAliasesConfigured.ShouldBeTrue();
        conflict.CommandsDefaultConflictAlias.ShouldBe("commands");
        (await migrated.Database.GetPendingMigrationsAsync()).ShouldBe([_independentChatTools]);
    }
}
