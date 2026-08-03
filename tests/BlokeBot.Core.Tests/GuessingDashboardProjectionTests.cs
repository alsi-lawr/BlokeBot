using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class GuessingDashboardProjectionTests
{
    [Test]
    public async Task RoundVotes_LoadingDashboard_ProjectsEmptyAndNonEmptyImmutableSnapshots()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var started = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        int hostId;
        int roundId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                EnabledFeatures = HostFeatureFlags.All,
                Login = "streamer",
                DisplayName = "Streamer",
                CreatedAtUtc = started,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            var profile = new GuessRoundProfile
            {
                HostId = host.Id,
                Name = "Default",
                Slug = "default",
                IsDefault = true,
                Options = [new GuessOption { Name = "blue", ReplyText = "Blue" }],
            };
            _ = db.Profiles.Add(profile);
            _ = await db.SaveChangesAsync();
            var round = new GuessRound
            {
                HostId = host.Id,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Open,
                StartedAtUtc = started,
            };
            _ = db.Rounds.Add(round);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            roundId = round.Id;
        }

        var service = new GuessingDashboardService(dbFactory);

        var empty = await service.LoadStateAsync(hostId, CancellationToken.None);

        _ = empty.CurrentRound.ShouldNotBeNull();
        empty.Votes.IsDefault.ShouldBeFalse();
        empty.Votes.IsEmpty.ShouldBeTrue();

        var earlier = started.AddMinutes(1);
        var later = started.AddMinutes(2);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Votes.AddRange(
                new GuessVote
                {
                    GuessRoundId = roundId,
                    Login = "earlier",
                    GuessName = "blue",
                    GuessedAtUtc = earlier,
                },
                new GuessVote
                {
                    GuessRoundId = roundId,
                    Login = "later",
                    GuessName = "red",
                    GuessedAtUtc = later,
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var populated = await service.LoadStateAsync(hostId, CancellationToken.None);

        populated.Votes.IsDefault.ShouldBeFalse();
        populated.Votes.ShouldBe([
            new GuessVoteView("later", "red", later),
            new GuessVoteView("earlier", "blue", earlier),
        ]);
    }
}
