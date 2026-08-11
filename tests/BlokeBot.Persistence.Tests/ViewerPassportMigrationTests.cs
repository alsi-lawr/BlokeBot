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
    private const string _passportMigration = "20260811035655_v0.10.0_ViewerPassports";
    private const string _loginHistoryMigration =
        "20260811051820_v0.10.0_ViewerPassportLoginHistory";
    private const string _ambiguityMigration =
        "20260811062237_v0.10.0_ViewerPassportAmbiguousLogins";

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
    public async Task Upgrade_BackfillsTheCurrentLoginAsTheFirstRememberedLogin()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_passportMigration);
            _ = await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures,
                     CommandsAliasesConfigured, CreatedAtUtc)
                VALUES
                    (1, 'host-id', 'host', 'Host', 0, 0, 0, '2026-08-11T00:00:00Z');

                INSERT INTO viewer_passports
                    (Id, HostId, TwitchUserId, Login, DisplayName, ProfileLine, Visibility,
                     HideAttendance, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (1, 1, 'viewer-id', 'remember_me', 'Viewer', '', 'Private', 1,
                     '2026-08-01T00:00:00Z', '2026-08-11T00:00:00Z');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        var login = await upgraded.ViewerPassportLogins.SingleAsync();
        login.HostId.ShouldBe(1);
        login.PassportId.ShouldBe(1);
        login.Login.ShouldBe("remember_me");
        login.FirstSeenAtUtc.ShouldBe(new DateTime(2026, 8, 1));
        login.LastSeenAtUtc.ShouldBe(new DateTime(2026, 8, 11));
    }

    [Test]
    public async Task Upgrade_BackfillsOnlyHostScopedConflictingAliasesAsAmbiguous()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_loginHistoryMigration);
            _ = await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures,
                     CommandsAliasesConfigured, CreatedAtUtc)
                VALUES
                    (1, 'first-host-id', 'first', 'First', 0, 0, 0, '2026-08-11T00:00:00Z'),
                    (2, 'second-host-id', 'second', 'Second', 0, 0, 0, '2026-08-11T00:00:00Z');

                INSERT INTO viewer_passports
                    (Id, HostId, TwitchUserId, Login, DisplayName, ProfileLine, Visibility,
                     HideAttendance, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (1, 1, 'viewer-a', 'viewer_a', 'Viewer A', '', 'Private', 1,
                     '2026-08-01T00:00:00Z', '2026-08-11T00:00:00Z'),
                    (2, 1, 'viewer-b', 'viewer_b', 'Viewer B', '', 'Private', 1,
                     '2026-08-02T00:00:00Z', '2026-08-11T00:00:00Z'),
                    (3, 2, 'viewer-c', 'shared', 'Viewer C', '', 'Private', 1,
                     '2026-08-03T00:00:00Z', '2026-08-11T00:00:00Z');

                INSERT INTO viewer_passport_logins
                    (HostId, PassportId, Login, FirstSeenAtUtc, LastSeenAtUtc)
                VALUES
                    (1, 1, 'shared', '2026-08-01T00:00:00Z', '2026-08-04T00:00:00Z'),
                    (1, 2, 'shared', '2026-08-05T00:00:00Z', '2026-08-06T00:00:00Z'),
                    (2, 3, 'shared', '2026-08-03T00:00:00Z', '2026-08-07T00:00:00Z');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        var ambiguous = await upgraded.ViewerPassportAmbiguousLogins.SingleAsync();
        ambiguous.HostId.ShouldBe(1);
        ambiguous.Login.ShouldBe("shared");
        ambiguous.DetectedAtUtc.ShouldBe(new DateTime(2026, 8, 5));
        (
            await upgraded.ViewerPassportAmbiguousLogins.CountAsync(value => value.HostId == 2)
        ).ShouldBe(0);
    }

    [Test]
    public async Task RollbackAndReupgrade_PreservesOwnerlessAmbiguityTombstones()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var migrate = await factory.CreateDbContextAsync())
        {
            await migrate.Database.MigrateAsync();
        }
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "host",
                DisplayName = "Host",
                CreatedAtUtc = new DateTime(2026, 8, 11),
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            var passport = new ViewerPassport
            {
                HostId = host.Id,
                TwitchUserId = "viewer-id",
                Login = "current_name",
                DisplayName = "Viewer",
                CreatedAtUtc = new DateTime(2026, 8, 1),
                UpdatedAtUtc = new DateTime(2026, 8, 11),
            };
            _ = seed.ViewerPassports.Add(passport);
            _ = await seed.SaveChangesAsync();
            seed.ViewerPassportLogins.AddRange(
                new()
                {
                    HostId = host.Id,
                    PassportId = passport.Id,
                    Login = "current_name",
                    FirstSeenAtUtc = new DateTime(2026, 8, 5),
                    LastSeenAtUtc = new DateTime(2026, 8, 11),
                },
                new()
                {
                    HostId = host.Id,
                    PassportId = passport.Id,
                    Login = "old_name",
                    FirstSeenAtUtc = new DateTime(2026, 8, 1),
                    LastSeenAtUtc = new DateTime(2026, 8, 5),
                }
            );
            _ = await seed.SaveChangesAsync();
            await ViewerPassportAmbiguityTombstones.PersistForPassportsAsync(
                seed,
                [passport.Id],
                default
            );
            _ = seed.ViewerPassports.Remove(passport);
            _ = await seed.SaveChangesAsync();
        }

        await using (var rollback = await factory.CreateDbContextAsync())
        {
            await rollback.GetService<IMigrator>().MigrateAsync(_loginHistoryMigration);
            (
                await rollback
                    .ViewerPassportAmbiguousLogins.OrderBy(value => value.Login)
                    .Select(value => value.Login)
                    .ToArrayAsync()
            ).ShouldBe(["current_name", "old_name"]);
            (await rollback.Database.GetAppliedMigrationsAsync()).ShouldNotContain(
                _ambiguityMigration
            );
            await rollback.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        (
            await upgraded
                .ViewerPassportAmbiguousLogins.OrderBy(value => value.Login)
                .Select(value => value.Login)
                .ToArrayAsync()
        ).ShouldBe(["current_name", "old_name"]);
        (await upgraded.Database.GetAppliedMigrationsAsync()).ShouldContain(_ambiguityMigration);
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
            _ = seed.ViewerPassportLogins.Add(
                new()
                {
                    HostId = host.Id,
                    PassportId = passport.Id,
                    Login = "viewer",
                    FirstSeenAtUtc = DateTime.UtcNow,
                    LastSeenAtUtc = DateTime.UtcNow,
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
            report.ChangedRows["viewer-passports.logins"].ShouldBe(1);
            report.ChangedRows["viewer-passports.attendance-days"].ShouldBe(1);
        }

        await using var verify = await factory.CreateDbContextAsync();
        (await verify.ViewerPassports.CountAsync()).ShouldBe(0);
        (await verify.ViewerPassportLogins.CountAsync()).ShouldBe(0);
        (await verify.ViewerPassportAttendanceDays.CountAsync()).ShouldBe(0);
    }
}
