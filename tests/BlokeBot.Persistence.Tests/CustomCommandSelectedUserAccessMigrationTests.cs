using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class CustomCommandSelectedUserAccessMigrationTests
{
    private const string _previousMigration = "20260803232049_v0.7.0_AutomationRuntime";
    private const string _migration = "20260804000549_v0.7.0_CustomCommandSelectedUserAccess";

    [Test]
    public async Task Upgrade_MapsLegacyPublicAndModeratorPoliciesWithoutAddingUsers()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_previousMigration);
            _ = await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, Login, DisplayName, EnabledFeatures, BotRuntimeState,
                     CommandsAliasesConfigured, CreatedAtUtc)
                VALUES
                    (1, 'streamer', 'Streamer', 65535, 0, 0, '2026-08-04T00:00:00Z');

                INSERT INTO custom_commands
                    (Id, HostId, Name, Enabled, ModeratorOnly, CooldownSeconds, CooldownScope,
                     InvocationLimit, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (1, 1, 'Public', 1, 0, 0, 'Global', 'Unlimited',
                     '2026-08-04T00:00:00Z', '2026-08-04T00:00:00Z'),
                    (2, 1, 'Moderator', 1, 1, 0, 'Global', 'Unlimited',
                     '2026-08-04T00:00:00Z', '2026-08-04T00:00:00Z');
                """
            );
            await before.GetService<IMigrator>().MigrateAsync(_migration);
        }

        await using var migrated = await factory.CreateDbContextAsync();
        var policies = await migrated
            .CustomCommands.OrderBy(command => command.Id)
            .Select(command => new { command.AllowEveryone, command.AllowModerators })
            .ToArrayAsync();

        policies[0].AllowEveryone.ShouldBeTrue();
        policies[0].AllowModerators.ShouldBeFalse();
        policies[1].AllowEveryone.ShouldBeFalse();
        policies[1].AllowModerators.ShouldBeTrue();
        (await migrated.CustomCommandAllowedUsers.CountAsync()).ShouldBe(0);
        (await migrated.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(_migration);
    }
}
