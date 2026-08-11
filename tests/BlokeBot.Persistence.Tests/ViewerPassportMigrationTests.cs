using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class ViewerPassportMigrationTests
{
    private const string _previousMigration = "20260810154030_v0.9.0_BingoOpaqueAssignments";

    [Test]
    public async Task Upgrade_AddsPassportSchemaAndLeavesEveryExistingHostOff()
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
                    (1, 'all-id', 'all', 'All', 0, 65535, 0, '2026-08-11T00:00:00Z'),
                    (2, 'none-id', 'none', 'None', 0, 0, 0, '2026-08-11T00:00:00Z');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        var hosts = await upgraded.Hosts.OrderBy(value => value.Id).ToArrayAsync();
        hosts.ShouldAllBe(host =>
            (host.EnabledFeatures & HostFeatureFlags.ViewerPassports) == HostFeatureFlags.None
        );
        (await upgraded.ViewerPassports.CountAsync()).ShouldBe(0);
        (await upgraded.ViewerPassportAttendanceDays.CountAsync()).ShouldBe(0);
        var tables = await upgraded
            .Database.SqlQuery<int>(
                $"""
                SELECT COUNT(*) AS "Value"
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('viewer_passports', 'viewer_passport_attendance_days')
                """
            )
            .SingleAsync();
        tables.ShouldBe(2);
    }

    [Test]
    public async Task ViewerPrivacyErasure_ReportsAndRemovesProfileAndAttendance()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "host",
                DisplayName = "Host",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            var passport = new ViewerPassport
            {
                HostId = host.Id,
                TwitchUserId = "viewer-id",
                Login = "viewer",
                DisplayName = "Viewer",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.ViewerPassports.Add(passport);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
            _ = seed.ViewerPassportAttendanceDays.Add(
                new()
                {
                    HostId = host.Id,
                    PassportId = passport.Id,
                    DateUtc = new DateOnly(2026, 8, 11),
                    FirstSeenAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        await using (var erase = await factory.CreateDbContextAsync())
        {
            var report = await ViewerPrivacyService.EraseAsync(
                erase,
                PrivacySubject.Create("viewer-id", "viewer"),
                hostId,
                default
            );
            report.ChangedRows["viewer-passports.profiles"].ShouldBe(1);
            report.ChangedRows["viewer-passports.attendance-days"].ShouldBe(1);
        }

        await using var verify = await factory.CreateDbContextAsync();
        (await verify.ViewerPassports.CountAsync()).ShouldBe(0);
        (await verify.ViewerPassportAttendanceDays.CountAsync()).ShouldBe(0);
    }
}
