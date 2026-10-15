using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandArgumentMigrationTests
{
    [Test]
    public async Task CommandArguments_UpgradeLeavesLegacyRowsUnschematizedAndRoundTripsExclusiveDecimalBounds()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        int hostId;
        await using (var previous = await database.CreateDbContextAsync())
        {
            await previous.Database.MigrateAsync("20261013163650_RecentChannelChatters");
            var host = new BotHost { Login = "arguments-upgrade", CreatedAtUtc = DateTime.UtcNow };
            _ = previous.Hosts.Add(host);
            _ = await previous.SaveChangesAsync();
            hostId = host.Id;
            var now = DateTime.UtcNow;
            _ = await previous.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO custom_commands ("HostId", "Name", "Enabled", "AllowEveryone", "AllowModerators", "CooldownSeconds", "CooldownScope", "InvocationLimit", "SingleArgument", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES ({hostId}, {"Legacy"}, {true}, {true}, {false}, {0}, {"Global"}, {"Unlimited"}, {true}, {now}, {now})
                """
            );
        }
        await new BlokeBotDatabaseInitializer(database).InitializeAsync(default);
        await using (var upgraded = await database.CreateDbContextAsync())
        {
            var legacy = await upgraded.CustomCommands.SingleAsync(command =>
                command.HostId == hostId
            );
            legacy.ArgumentSchema.ShouldBeNull();
            legacy.SingleArgument.ShouldBeTrue();
            legacy.ArgumentSchema = new([
                new(
                    "seconds",
                    CustomCommandArgumentKind.Number,
                    0m,
                    123.45678901234567890123456789m
                ),
                new("name", CustomCommandArgumentKind.RemainingText, MaximumWords: 10),
            ]);
            _ = await upgraded.SaveChangesAsync();
        }
        await using var reopened = await database.CreateDbContextAsync();
        var stored = (await reopened.CustomCommands.SingleAsync()).ArgumentSchema.ShouldNotBeNull();
        stored.Fields[0].ExclusiveMinimum.ShouldBe(0m);
        stored.Fields[0].Maximum.ShouldBe(123.45678901234567890123456789m);
        stored.Fields[1].MaximumWords.ShouldBe(10);
        stored.Fields[1].Kind.ShouldBe(CustomCommandArgumentKind.RemainingText);
        stored = stored with
        {
            Fields = stored.Fields.SetItem(0, stored.Fields[0] with { ExclusiveMinimum = -1.25m }),
        };
        (await reopened.CustomCommands.SingleAsync()).ArgumentSchema = stored;
        _ = await reopened.SaveChangesAsync();
        await using var final = await database.CreateDbContextAsync();
        (await final.CustomCommands.SingleAsync())
            .ArgumentSchema!.Fields[0]
            .ExclusiveMinimum.ShouldBe(-1.25m);
    }
}
