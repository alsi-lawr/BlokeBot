using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class CustomStoredValueMigrationTests
{
    [Test]
    public async Task Sqlite_UpgradePreservesLegacyCountersAndDefaultsNewModeBeforeScopedWrites()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await UpgradeAsync(database, "20261006192842_WatchTimePoints");
    }

    [Test, Explicit]
    public async Task PostgreSql_UpgradePreservesLegacyCountersAndDefaultsNewModeBeforeScopedWrites()
    {
        await using var lease = await PointProviderFixture.PostgreSqlAsync();
        await using var db = lease.CreateDbContext();
        _ = await db.Database.EnsureDeletedAsync();
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseNpgsql(
                db.Database.GetConnectionString()!,
                postgres => postgres.MigrationsAssembly("BlokeBot.Persistence.PostgreSql")
            )
            .Options;
        await UpgradeAsync(new MigrationFactory(options), "20261006192903_WatchTimePoints");
    }

    private static async Task UpgradeAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        string previous
    )
    {
        int hostId;
        await using (var before = await database.CreateDbContextAsync())
        {
            await before.Database.MigrateAsync(previous);
            var host = new BotHost
            {
                Login = "migration",
                DisplayName = "Migration",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = before.Hosts.Add(host);
            _ = await before.SaveChangesAsync();
            hostId = host.Id;
            var now = DateTime.UtcNow;
            _ = before.CustomCounters.Add(
                new()
                {
                    HostId = hostId,
                    Name = "legacy",
                    Value = 49,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
            _ = await before.SaveChangesAsync();
            _ = await before.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_commands ("HostId", "Name", "Enabled", "AllowEveryone", "AllowModerators", "CooldownSeconds", "CooldownScope", "InvocationLimit", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES ({hostId}, {"Existing command"}, {true}, {true}, {false}, {0}, {"Global"}, {"Unlimited"}, {now}, {now})
                """
            );
        }
        await new BlokeBotDatabaseInitializer(database).InitializeAsync(default);
        await new BlokeBotDatabaseInitializer(database).InitializeAsync(default);
        var values = new CustomStoredValueService(database);
        var definition = await CustomCommandExecutionTests.DeclareAsync(
            values,
            hostId,
            new string('n', 8192),
            CustomValueScope.Global,
            CustomValueKind.Number,
            "4"
        );
        var original = (
            await values.ValueAsync(hostId, new(definition.Id, string.Empty, string.Empty), default)
        )!;
        (
            await values.SaveValueAsync(hostId, original, CustomValueKind.Number, "7", default)
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CustomCounters.SingleAsync()).Value.ShouldBe(49);
        (await verify.CustomCommands.SingleAsync()).SingleArgument.ShouldBeFalse();
        (await verify.CustomStoredValues.SingleAsync()).Number.ShouldBe(7);
        (await verify.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    private sealed class MigrationFactory(DbContextOptions<BlokeBotDbContext> options)
        : IDbContextFactory<BlokeBotDbContext>
    {
        public BlokeBotDbContext CreateDbContext() => new(options);
    }
}
