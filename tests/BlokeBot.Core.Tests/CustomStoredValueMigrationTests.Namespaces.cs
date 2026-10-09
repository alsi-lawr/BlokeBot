using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class CustomStoredValueMigrationTests
{
    [Test]
    public async Task Sqlite_NamespaceUpgradePreservesExistingValuesAndResultsBeforeCoexistence()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await NamespaceUpgradeAsync(database, "20261008174342_ScopedCustomCommandValues");
    }

    [Test, Explicit]
    public async Task PostgreSql_NamespaceUpgradePreservesExistingValuesAndResultsBeforeCoexistence()
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
        await NamespaceUpgradeAsync(
            new MigrationFactory(options),
            "20261008174636_ScopedCustomCommandValues"
        );
    }

    private static async Task NamespaceUpgradeAsync(
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
                Login = "streamer",
                DisplayName = "Streamer",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = before.Hosts.Add(host);
            _ = await before.SaveChangesAsync();
            hostId = host.Id;
        }
        var values = new CustomStoredValueService(database);
        var scalar = await CustomCommandExecutionTests.DeclareAsync(
            values,
            hostId,
            "profile",
            CustomValueScope.User,
            CustomValueKind.Number,
            "4"
        );
        var original = (
            await values.ValueAsync(hostId, new(scalar.Id, "stable-viewer", string.Empty), default)
        )!;
        (
            await values.SaveValueAsync(hostId, original, CustomValueKind.Number, "8", default)
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        await using (var before = await database.CreateDbContextAsync())
        {
            _ = before.CustomCommandComputedResults.Add(
                new()
                {
                    HostId = hostId,
                    CommandId = 123,
                    InvocationId = "before-upgrade",
                    InvocationHash = CustomValueIdentity.Hash("before-upgrade"),
                    ViewerId = "stable-viewer",
                    Reply = "before:8",
                    ReplyEligible = false,
                }
            );
            _ = await before.SaveChangesAsync();
        }
        await new BlokeBotDatabaseInitializer(database).InitializeAsync(default);
        await new BlokeBotDatabaseInitializer(database).InitializeAsync(default);
        var dictionary = await CustomCommandExecutionTests.DeclareAsync(
            values,
            hostId,
            "profile",
            CustomValueScope.User,
            CustomValueKind.Dictionary,
            "missing"
        );
        var entry = (
            await values.ValueAsync(hostId, new(dictionary.Id, "stable-viewer", "game"), default)
        )!;
        (
            await values.SaveValueAsync(hostId, entry, CustomValueKind.Text, "Celeste", default)
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        (await values.ValueAsync(hostId, original.Target, default))!.Value.ShouldBe("8");
        (await values.ValueAsync(hostId, entry.Target, default))!.Value.ShouldBe("Celeste");
        await using var verify = await database.CreateDbContextAsync();
        var result = await verify.CustomCommandComputedResults.SingleAsync();
        result.Reply.ShouldBe("before:8");
        result.ReplyEligible.ShouldBeFalse();
        (await verify.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }
}
