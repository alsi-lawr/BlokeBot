using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class GuessingHistoryTests
{
    [Test]
    public async Task LiveAndCompletedRounds_LoadingRecentHistory_ReturnsCompletedRoundWithWinnerCounts()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        BotHost host;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            host = new BotHost
            {
                EnabledFeatures = HostFeatureFlags.All,
                Login = "streamer",
                DisplayName = "Streamer",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();

            var profile = new GuessRoundProfile
            {
                HostId = host.Id,
                Name = "Score",
                Slug = "score",
                IsDefault = true,
                ReplySettings = new BotReplySettings(),
            };
            _ = db.Profiles.Add(profile);
            _ = await db.SaveChangesAsync();

            _ = db.Rounds.Add(
                new GuessRound
                {
                    HostId = host.Id,
                    GuessRoundProfileId = profile.Id,
                    Status = GuessRoundStatus.Open,
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                }
            );
            _ = db.Rounds.Add(
                new GuessRound
                {
                    HostId = host.Id,
                    GuessRoundProfileId = profile.Id,
                    Status = GuessRoundStatus.Completed,
                    StartedAtUtc = DateTime.UtcNow.AddHours(-1),
                    ClosedAtUtc = DateTime.UtcNow.AddMinutes(-30),
                    WinningName = "blue",
                    Votes =
                    [
                        new GuessVote
                        {
                            Login = "one",
                            GuessName = "blue",
                            GuessedAtUtc = DateTime.UtcNow.AddMinutes(-50),
                        },
                        new GuessVote
                        {
                            Login = "two",
                            GuessName = "red",
                            GuessedAtUtc = DateTime.UtcNow.AddMinutes(-45),
                        },
                    ],
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var service = new GuessingHistoryService(dbFactory);
        var entries = await service.LoadRecentCompletedRoundsAsync(
            host.Id,
            10,
            CancellationToken.None
        );

        var entry = entries.Single();
        entry.ProfileName.ShouldBe("Score");
        entry.Lifecycle.WinningName.ShouldBe("blue");
        entry.GuessCount.ShouldBe(2);
        entry.CorrectGuessCount.ShouldBe(1);
    }
}
