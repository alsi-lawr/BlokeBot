using BlokeBot.Features.Guessing.History;
using BlokeBot.Persistence.Models;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class GuessingHistoryTests
{
    [Test]
    public async Task Recent_completed_rounds_excludes_live_rounds_and_counts_winners()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        BotHost host;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            host = new BotHost
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
                ReplySettings = new BotReplySettings(),
            };
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();

            db.Rounds.Add(
                new GuessRound
                {
                    HostId = host.Id,
                    GuessRoundProfileId = profile.Id,
                    Status = GuessRoundStatus.Open,
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                }
            );
            db.Rounds.Add(
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
            await db.SaveChangesAsync();
        }

        var service = new GuessingHistoryService(dbFactory);
        var entries = await service.LoadRecentCompletedRoundsAsync(
            host.Id,
            10,
            CancellationToken.None
        );

        var entry = entries.Single();
        entry.ProfileName.ShouldBe("Score");
        entry.WinningName.ShouldBe("blue");
        entry.GuessCount.ShouldBe(2);
        entry.CorrectGuessCount.ShouldBe(1);
    }
}
