using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointsGiveawayProjectionTests
{
    [Test]
    public async Task ActiveGiveawayWithoutEntries_LoadingView_ProjectsEmptyImmutableCollections()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var started = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        _ = db.PointsGiveaways.Add(
            new PointsGiveaway
            {
                HostId = host.Id,
                Status = PointsGiveawayStatus.Active,
                StartedAtUtc = started,
                EndsAtUtc = started.AddMinutes(5),
            }
        );
        _ = await db.SaveChangesAsync();

        var view = await PointsGiveawayQueries.LoadActiveViewAsync(
            db,
            host.Id,
            CancellationToken.None
        );

        _ = view.ShouldNotBeNull();
        view!.Entrants.IsDefault.ShouldBeFalse();
        view.Entrants.IsEmpty.ShouldBeTrue();
        view.Winners.IsDefault.ShouldBeFalse();
        view.Winners.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public async Task ActiveGiveaway_LoadingView_ProjectsOrderedImmutableCollections()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var started = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        var giveaway = new PointsGiveaway
        {
            HostId = host.Id,
            Status = PointsGiveawayStatus.Active,
            StartedAtUtc = started,
            EndsAtUtc = started.AddMinutes(5),
        };
        _ = db.PointsGiveaways.Add(giveaway);
        _ = await db.SaveChangesAsync();
        db.PointsGiveawayEntrants.AddRange(
            new PointsGiveawayEntrant
            {
                GiveawayId = giveaway.Id,
                Login = "later",
                JoinedAtUtc = started.AddMinutes(2),
            },
            new PointsGiveawayEntrant
            {
                GiveawayId = giveaway.Id,
                Login = "earlier",
                JoinedAtUtc = started.AddMinutes(1),
            }
        );
        db.PointsGiveawayWinners.AddRange(
            new PointsGiveawayWinner
            {
                GiveawayId = giveaway.Id,
                Login = "earlier",
                Payout = "10",
            },
            new PointsGiveawayWinner
            {
                GiveawayId = giveaway.Id,
                Login = "later",
                Payout = "20",
            }
        );
        _ = await db.SaveChangesAsync();

        var view = await PointsGiveawayQueries.LoadActiveViewAsync(
            db,
            host.Id,
            CancellationToken.None
        );

        _ = view.ShouldNotBeNull();
        _ = view!.Lifecycle.ShouldBeOfType<PointsGiveawayLifecycle.Active>();
        view.Entrants.ShouldBe(["earlier", "later"]);
        view.Winners.ShouldBe([
            new PointsGiveawayWinnerView("earlier", PointAmount.ParseAbsolute("10")),
            new PointsGiveawayWinnerView("later", PointAmount.ParseAbsolute("20")),
        ]);
    }

    [Test]
    public async Task ActiveGiveawayWithCompletionTime_LoadingView_FailsDataIntegrity()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        _ = db.PointsGiveaways.Add(
            new PointsGiveaway
            {
                HostId = host.Id,
                Status = PointsGiveawayStatus.Active,
                StartedAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddMinutes(5),
                CompletedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();

        _ = await Should.ThrowAsync<PersistenceDataIntegrityException>(() =>
            PointsGiveawayQueries.LoadActiveViewAsync(db, host.Id, CancellationToken.None)
        );
    }
}
