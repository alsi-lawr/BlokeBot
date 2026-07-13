using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class HostLifecycleTests
{
    [Test]
    public async Task HostWithOwnedGraph_Removing_CascadesHostDataAndPreservesSiteData()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostedChannelGraphAsync(dbFactory);
        var events = TestEventBus.Create<AppEventKind>();
        var eventCount = 0;
        events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.HostRemoval"),
            (_, _) =>
            {
                eventCount++;
                return ValueTask.CompletedTask;
            }
        );
        var service = new BotHostRemovalService(dbFactory, new HostedChannelChangeNotifier(events));

        var removed = await service.RemoveAsync(hostId, CancellationToken.None);

        removed.ShouldBeTrue();
        eventCount.ShouldBe(1);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.Hosts.CountAsync()).ShouldBe(0);
        (await db.HostModAccessSettings.CountAsync()).ShouldBe(0);
        (await db.HostModAccessEntries.CountAsync()).ShouldBe(0);
        (await db.CommandAliases.CountAsync()).ShouldBe(0);
        (await db.Profiles.CountAsync()).ShouldBe(0);
        (await db.ReplySettings.CountAsync()).ShouldBe(0);
        (await db.GuessOptions.CountAsync()).ShouldBe(0);
        (await db.Rounds.CountAsync()).ShouldBe(0);
        (await db.Votes.CountAsync()).ShouldBe(0);
        (await db.PointsSettings.CountAsync()).ShouldBe(0);
        (await db.PointBalances.CountAsync()).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync()).ShouldBe(0);
        (await db.PointsGiveaways.CountAsync()).ShouldBe(0);
        (await db.PointsGiveawayEntrants.CountAsync()).ShouldBe(0);
        (await db.PointsGiveawayWinners.CountAsync()).ShouldBe(0);
        (await db.SiteAccessEntries.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task MissingHost_Removing_ReturnsFalseWithoutEvent()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var events = TestEventBus.Create<AppEventKind>();
        var eventCount = 0;
        events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.MissingHostRemoval"),
            (_, _) =>
            {
                eventCount++;
                return ValueTask.CompletedTask;
            }
        );
        var service = new BotHostRemovalService(dbFactory, new HostedChannelChangeNotifier(events));

        var removed = await service.RemoveAsync(123, CancellationToken.None);

        removed.ShouldBeFalse();
        eventCount.ShouldBe(0);
    }

    [Test]
    public async Task NewHost_Provisioning_PublishesHostedChannelChange()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var events = TestEventBus.Create<AppEventKind>();
        var eventCount = 0;
        events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.HostProvisioning"),
            (_, _) =>
            {
                eventCount++;
                return ValueTask.CompletedTask;
            }
        );
        var service = new BotHostProvisioningService(
            dbFactory,
            new HostedChannelChangeNotifier(events),
            []
        );

        var hostId = await service.EnsureHostAsync(
            "streamer",
            twitchUserId: "123",
            displayName: "Streamer",
            profileImageUrl: null,
            CancellationToken.None
        );

        hostId.ShouldBeGreaterThan(0);
        eventCount.ShouldBe(1);
    }

    private static async Task<int> SeedHostedChannelGraphAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        db.SiteAccessEntries.Add(
            new SiteAccessEntry
            {
                Login = "viewer",
                Kind = AccessListEntryKind.Whitelist,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        db.HostModAccessSettings.Add(new HostModAccessSettings { HostId = host.Id });
        db.HostModAccessEntries.Add(
            new HostModAccessEntry
            {
                HostId = host.Id,
                Login = "moderator",
                Kind = AccessListEntryKind.Whitelist,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
        db.CommandAliases.Add(
            new CommandAlias
            {
                HostId = host.Id,
                Kind = AppCommandKind.Guess,
                Alias = "!guess",
            }
        );
        db.PointsSettings.Add(new PointsSettings { HostId = host.Id });
        db.PointBalances.Add(
            new PointBalance
            {
                HostId = host.Id,
                Login = "viewer",
                Amount = "10",
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        db.PointLedgerEntries.Add(
            new PointLedgerEntry
            {
                HostId = host.Id,
                CreatedAtUtc = DateTime.UtcNow,
                Kind = PointLedgerKind.Add,
                Login = "viewer",
                Delta = "10",
                BalanceAfter = "10",
            }
        );
        db.PointsGiveaways.Add(
            new PointsGiveaway
            {
                HostId = host.Id,
                Status = PointsGiveawayStatus.Completed,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                EndsAtUtc = DateTime.UtcNow.AddMinutes(-1),
                CompletedAtUtc = DateTime.UtcNow,
                Entrants =
                [
                    new PointsGiveawayEntrant { Login = "viewer", JoinedAtUtc = DateTime.UtcNow },
                ],
                Winners = [new PointsGiveawayWinner { Login = "viewer", Payout = "10" }],
            }
        );

        var profile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
            ReplySettings = new BotReplySettings { AvailableGuessesReply = "Guesses: {options}" },
            Options = [new GuessOption { Name = "red", ReplyText = "Red" }],
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        var round = new GuessRound
        {
            HostId = host.Id,
            GuessRoundProfileId = profile.Id,
            Status = GuessRoundStatus.Open,
            StartedAtUtc = DateTime.UtcNow,
        };
        db.Rounds.Add(round);
        await db.SaveChangesAsync();

        db.Votes.Add(
            new GuessVote
            {
                GuessRoundId = round.Id,
                Login = "viewer",
                GuessName = "red",
                GuessedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        return host.Id;
    }
}
