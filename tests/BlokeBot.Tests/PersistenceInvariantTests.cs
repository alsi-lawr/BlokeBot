using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PersistenceInvariantTests
{
    [Test]
    public async Task Database_rejects_duplicate_active_giveaways_for_host()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);

        db.PointsGiveaways.AddRange(
            Giveaway(hostId, PointsGiveawayStatus.Active),
            Giveaway(hostId, PointsGiveawayStatus.Active)
        );

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task Database_rejects_duplicate_unresolved_guessing_rounds_for_host()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);
        var profileId = await SeedProfileAsync(db, hostId);

        db.Rounds.AddRange(
            Round(hostId, profileId, GuessRoundStatus.Open),
            Round(hostId, profileId, GuessRoundStatus.Closed)
        );

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task Database_rejects_duplicate_default_profiles_for_host()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);
        await SeedProfileAsync(db, hostId);

        db.Profiles.Add(
            new GuessRoundProfile
            {
                HostId = hostId,
                Name = "Other",
                Slug = "other",
                IsDefault = true,
            }
        );

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task Database_rejects_invalid_status_and_kind_values()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var hostId = await SeedHostAsync(db);

        await Should.ThrowAsync<SqliteException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO points_giveaways
                    (HostId, Status, StartedAtUtc, EndsAtUtc, MinimumPayout, MaximumPayout, WinnerCount, Eligibility)
                VALUES
                    ({hostId}, 'Bogus', {DateTime.UtcNow}, {DateTime.UtcNow.AddMinutes(5)}, '10', '100', 1, 'everyone')
                """
            )
        );

        await Should.ThrowAsync<SqliteException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO command_aliases (HostId, Kind, Alias)
                VALUES ({hostId}, 'Bogus', 'bogus')
                """
            )
        );
    }

    private static PointsGiveaway Giveaway(int hostId, PointsGiveawayStatus status) =>
        new()
        {
            HostId = hostId,
            Status = status,
            StartedAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddMinutes(5),
        };

    private static GuessRound Round(int hostId, int profileId, GuessRoundStatus status) =>
        new()
        {
            HostId = hostId,
            GuessRoundProfileId = profileId,
            Status = status,
            StartedAtUtc = DateTime.UtcNow,
            ClosedAtUtc = status == GuessRoundStatus.Open ? null : DateTime.UtcNow,
        };

    private static async Task<int> SeedHostAsync(BlokeBotDbContext db)
    {
        var host = new BotHost
        {
            Login = $"host-{Guid.NewGuid():N}",
            DisplayName = "Host",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<int> SeedProfileAsync(BlokeBotDbContext db, int hostId)
    {
        var profile = new GuessRoundProfile
        {
            HostId = hostId,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }
}
