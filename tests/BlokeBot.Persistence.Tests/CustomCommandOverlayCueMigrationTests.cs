using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class CustomCommandOverlayCueMigrationTests
{
    private const string _previous = "20260731043353_v0.6.0_OverlayCues";
    private const string _migration = "20260731064005_v0.6.0_CustomCommandOverlayCues";

    [Test]
    public async Task Migration_AddsCueActionWithoutChangingLegacyActionsAndEnforcesPayloadShape()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using var db = await factory.CreateDbContextAsync();
        await db.GetService<IMigrator>().MigrateAsync(_previous);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO hosts
                (Id, Login, DisplayName, EnabledFeatures, CommandsAliasesConfigured,
                 BotRuntimeState, CreatedAtUtc)
            VALUES (1, 'host', 'Host', 65535, 0, 0, '2026-07-31T00:00:00Z');

            INSERT INTO custom_commands
                (Id, HostId, Name, Enabled, ModeratorOnly, CooldownSeconds, CooldownScope,
                 InvocationLimit, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (1, 1, 'Message', 1, 0, 0, 'Global', 'Unlimited',
                 '2026-07-31T00:00:00Z', '2026-07-31T00:00:00Z'),
                (2, 1, 'Counter', 1, 0, 0, 'Global', 'Unlimited',
                 '2026-07-31T00:00:00Z', '2026-07-31T00:00:00Z');

            INSERT INTO custom_counters (Id, HostId, Name, Value, CreatedAtUtc, UpdatedAtUtc)
            VALUES (1, 1, 'Count', 0, '2026-07-31T00:00:00Z', '2026-07-31T00:00:00Z');

            INSERT INTO custom_command_actions (CustomCommandId, HostId, ActionType, CounterId)
            VALUES (1, 1, 'Message', NULL), (2, 1, 'Counter', 1);
            """
        );

        await db.GetService<IMigrator>().MigrateAsync(_migration);

        (
            await db
                .Database.SqlQueryRaw<string>(
                    """SELECT ActionType AS Value FROM custom_command_actions ORDER BY CustomCommandId"""
                )
                .ToArrayAsync()
        ).ShouldBe(["Message", "Counter"]);
        var targetId = Guid.NewGuid();
        var cueId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO custom_commands
                (Id, HostId, Name, Enabled, ModeratorOnly, CooldownSeconds, CooldownScope,
                 InvocationLimit, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (3, 1, 'Cue', 1, 0, 0, 'Global', 'Unlimited',
                 '2026-07-31T00:00:00Z', '2026-07-31T00:00:00Z');

            INSERT INTO custom_command_actions
                (CustomCommandId, HostId, ActionType, CounterId, TargetOverlayPublicId,
                 CuePublicId, QueuePolicy, ReplyOrder)
            VALUES
                (3, 1, 'OverlayCue', NULL, {targetId}, {cueId},
                 'enqueue', 'after');
            """
        );
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO custom_commands
                (Id, HostId, Name, Enabled, ModeratorOnly, CooldownSeconds, CooldownScope,
                 InvocationLimit, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (4, 1, 'Invalid cue', 1, 0, 0, 'Global', 'Unlimited',
                 '2026-07-31T00:00:00Z', '2026-07-31T00:00:00Z');
            """
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO custom_command_actions
                    (CustomCommandId, HostId, ActionType, CounterId, TargetOverlayPublicId,
                     CuePublicId, QueuePolicy, ReplyOrder)
                VALUES (4, 1, 'OverlayCue', NULL, NULL, NULL, 'invalid', 'after');
                """
            )
        );
        db.GetService<IMigrationsAssembly>().Migrations.Count.ShouldBe(19);
        (await db.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(_migration);
        (await db.Database.GetPendingMigrationsAsync()).ShouldBe([
            "20260731083003_v0.6.0_GiveawayOverlay",
            "20260731110140_v0.6.0_EventFeedOverlay",
        ]);
    }
}
