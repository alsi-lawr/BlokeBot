using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class AutomaticRaidShoutoutPersistenceTests
{
    [Test]
    public async Task CurrentMigration_HasNoPendingModelAndDatabaseDefaults()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        db.GetService<IMigrationsAssembly>().Migrations.Count.ShouldBe(18);
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        var hostId = await SeedHostAsync(db, "host");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO automatic_raid_shoutout_settings (HostId, UpdatedAtUtc) VALUES ({hostId}, {DateTime.UtcNow});"
        );

        var row = await db.AutomaticRaidShoutoutSettings.AsNoTracking().SingleAsync();
        row.Enabled.ShouldBeFalse();
        row.MinimumViewerCount.ShouldBe(1);
        row.Mechanism.ShouldBe(AutomaticRaidShoutoutMechanism.Native);
        row.ChatPresentation.ShouldBe(AutomaticRaidChatPresentation.Regular);
        row.AnnouncementColor.ShouldBe(TwitchAnnouncementColor.Primary);
        row.PinDurationSeconds.ShouldBeNull();
        row.MessageTemplate.ShouldBe(AutomaticRaidShoutoutDefaults.MessageTemplate);

        foreach (var code in Enum.GetValues<AutomaticRaidShoutoutResultCode>())
        {
            var status = code switch
            {
                AutomaticRaidShoutoutResultCode.Delivered =>
                    AutomaticRaidShoutoutOutcomeStatus.Delivered,
                AutomaticRaidShoutoutResultCode.Ambiguous =>
                    AutomaticRaidShoutoutOutcomeStatus.Ambiguous,
                _ => AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
            };
            db.AutomaticRaidShoutoutOutcomes.Add(
                new AutomaticRaidShoutoutOutcome
                {
                    HostId = hostId,
                    ProviderMessageId = $"result-{code}",
                    SourceTwitchUserId = "raider-id",
                    SourceLogin = "raider",
                    SourceDisplayName = "Raider",
                    ViewerCount = 1,
                    Status = status,
                    ResultCode = code,
                    MessageTimestampUtc = DateTime.UtcNow,
                    ClaimedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                }
            );
        }
        await db.SaveChangesAsync();
        (await db.AutomaticRaidShoutoutOutcomes.CountAsync()).ShouldBe(
            Enum.GetValues<AutomaticRaidShoutoutResultCode>().Length
        );
    }

    [Test]
    public async Task SettingsConstraintsAndHostUniqueClaim_AreDatabaseEnforced()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await factory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db, "host");
        var otherHostId = await SeedHostAsync(db, "other");

        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO automatic_raid_shoutout_settings (HostId, MinimumViewerCount, UpdatedAtUtc) VALUES ({hostId}, 0, {DateTime.UtcNow});"
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO automatic_raid_shoutout_settings (HostId, Mechanism, UpdatedAtUtc) VALUES ({hostId}, {"Bogus"}, {DateTime.UtcNow});"
            )
        );
        db.AutomaticRaidProcessedEvents.AddRange(Claim(hostId, "same"), Claim(otherHostId, "same"));
        await db.SaveChangesAsync();
        db.AutomaticRaidProcessedEvents.Add(Claim(hostId, "same"));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task OutcomeStateConstraint_RejectsInconsistentTerminalState()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await factory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db, "host");
        var messageId = "message";
        var raiderId = "raider-id";
        var raiderLogin = "raider";
        var raiderDisplayName = "Raider";
        var delivered = "Delivered";

        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO automatic_raid_shoutout_outcomes
                    (HostId, ProviderMessageId, SourceTwitchUserId, SourceLogin, SourceDisplayName,
                     ViewerCount, Status, ResultCode, MessageTimestampUtc, ClaimedAtUtc, CompletedAtUtc)
                VALUES
                    ({hostId}, {messageId}, {raiderId}, {raiderLogin}, {raiderDisplayName},
                     1, {delivered}, NULL, {DateTime.UtcNow}, {DateTime.UtcNow}, {DateTime.UtcNow});
                """
            )
        );
    }

    private static AutomaticRaidProcessedEvent Claim(int hostId, string messageId)
    {
        var now = DateTime.UtcNow;
        return new()
        {
            HostId = hostId,
            ProviderMessageId = messageId,
            ClaimedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(2),
        };
    }

    private static async Task<int> SeedHostAsync(BlokeBotDbContext db, string login)
    {
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
