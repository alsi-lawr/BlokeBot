using BlokeBot.Eventing;
using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class GuessingPointRewardTests
{
    [Test]
    public async Task WinningGuessReward_SavingConfiguration_PersistsValue()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedRoundAsync(dbFactory, "0");
        var service = new GuessingConfigurationService(
            dbFactory,
            new CommandAliasRegistry(),
            new GuessingChangeNotifier(TestEventBus.Create<AppEventKind>())
        );
        var config = await service.LoadConfigurationAsync(
            seed.HostId,
            seed.ProfileId,
            CancellationToken.None
        );
        config.Profile.WinningGuessPointReward = "250";

        await service.SaveConfigurationAsync(seed.HostId, config, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.Profiles.SingleAsync(x => x.Id == seed.ProfileId);
        profile.WinningGuessPointReward.ShouldBe("250");
    }

    [Test]
    public async Task CorrectGuessesWithReward_DeclaringWinner_AwardsBalancesAndLedgerEntries()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedRoundAsync(dbFactory, "25");
        var service = RoundService(dbFactory);

        var result = await service.DeclareWinnerAsync(seed.HostId, "blue", CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var balances = await db
            .PointBalances.OrderBy(x => x.Login)
            .ToListAsync(CancellationToken.None);
        var ledger = await db
            .PointLedgerEntries.OrderBy(x => x.Login)
            .ToListAsync(CancellationToken.None);
        var round = await db.Rounds.SingleAsync(x => x.Id == seed.RoundId);

        result.Succeeded.ShouldBeTrue();
        result.Message.ShouldBe(
            "blue wins. Correct guesses: one, three. Each winner gets 25 beans."
        );
        round.Status.ShouldBe(GuessRoundStatus.Completed);
        round.WinningName.ShouldBe("blue");
        balances.Select(x => (x.Login, x.Amount)).ShouldBe([("one", "25"), ("three", "25")]);
        ledger
            .Select(x => (x.Kind, x.Login, x.Delta, x.BalanceAfter, x.Note))
            .ShouldBe([
                ("GuessWin", "one", "25", "25", $"guess round {seed.RoundId}"),
                ("GuessWin", "three", "25", "25", $"guess round {seed.RoundId}"),
            ]);
    }

    [Test]
    public async Task CorrectGuessesWithZeroReward_DeclaringWinner_CreatesNoPointRows()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedRoundAsync(dbFactory, "0");
        var service = RoundService(dbFactory);

        var result = await service.DeclareWinnerAsync(seed.HostId, "blue", CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        result.Succeeded.ShouldBeTrue();
        result.Message.ShouldBe("blue wins. Correct guesses: one, three.");
        (await db.PointBalances.CountAsync(CancellationToken.None)).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync(CancellationToken.None)).ShouldBe(0);
    }

    private static GuessingRoundService RoundService(SqliteBlokeBotDbFactory dbFactory) =>
        new(
            dbFactory,
            new GuessingChangeNotifier(TestEventBus.Create<AppEventKind>()),
            new PointBalanceService(dbFactory),
            new PointsChangeNotifier(TestEventBus.Create<AppEventKind>())
        );

    private static async Task<RoundSeed> SeedRoundAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string reward
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();

        var profile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Score",
            Slug = "score",
            IsDefault = true,
            WinningGuessPointReward = reward,
            ReplySettings = new BotReplySettings
            {
                WinnerReply = "{name} wins. Correct guesses: {winners}.{reward_text}",
                NoWinnersReply = "{name} wins. Nobody guessed correctly.",
            },
            Options =
            [
                new GuessOption { Name = "blue", ReplyText = "Blue" },
                new GuessOption { Name = "red", ReplyText = "Red" },
            ],
        };
        db.Profiles.Add(profile);
        db.PointsSettings.Add(new PointsSettings { HostId = host.Id, PointLabel = "beans" });
        await db.SaveChangesAsync();

        var round = new GuessRound
        {
            HostId = host.Id,
            GuessRoundProfileId = profile.Id,
            Status = GuessRoundStatus.Open,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Votes =
            [
                new GuessVote
                {
                    Login = "one",
                    GuessName = "blue",
                    GuessedAtUtc = DateTime.UtcNow.AddMinutes(-4),
                },
                new GuessVote
                {
                    Login = "two",
                    GuessName = "red",
                    GuessedAtUtc = DateTime.UtcNow.AddMinutes(-3),
                },
                new GuessVote
                {
                    Login = "three",
                    GuessName = "blue",
                    GuessedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                },
            ],
        };
        db.Rounds.Add(round);
        await db.SaveChangesAsync();
        return new RoundSeed(host.Id, profile.Id, round.Id);
    }

    private sealed record RoundSeed(int HostId, int ProfileId, int RoundId);
}
