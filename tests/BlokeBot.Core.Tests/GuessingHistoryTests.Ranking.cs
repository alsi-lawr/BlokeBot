using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class GuessingHistoryRankingTests
{
    private static readonly DateTime _epoch = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Sqlite_RankingPrecedesPaginationAndPublicLeaderUsesAllPriorities() =>
        await RankingAsync(await PointProviderFixture.SqliteAsync());

    [Test, Explicit]
    public async Task PostgreSql_RankingPrecedesPaginationAndPublicLeaderUsesAllPriorities() =>
        await RankingAsync(await PointProviderFixture.PostgreSqlAsync());

    [Test]
    public async Task Sqlite_FilteredCountsAndFirstWinsUseTheSameVoteScope() =>
        await FilteredAsync(await PointProviderFixture.SqliteAsync());

    [Test, Explicit]
    public async Task PostgreSql_FilteredCountsAndFirstWinsUseTheSameVoteScope() =>
        await FilteredAsync(await PointProviderFixture.PostgreSqlAsync());

    private static async Task RankingAsync(PointProviderFixture fixture)
    {
        await using var owned = fixture;
        int hostId;
        await using (var db = fixture.CreateDbContext())
        {
            var profile = await ProfileAsync(db, "ranking");
            hostId = profile.HostId;
            AddPlayer(db, profile, "c-early", 3, 6, 10, 2);
            AddPlayer(db, profile, "z-tied", 3, 6, 10, 3);
            AddPlayer(db, profile, "b-later", 3, 6, 20, 1);
            AddPlayer(db, profile, "a-lower-rate", 3, 9, 5, 1);
            AddPlayer(db, profile, "better-rate-fewer-wins", 2, 3, 5, 1);
            AddPlayer(db, profile, "incomplete-attempts", 1, 4, 30, 1);
            AddPlayer(db, profile, "a-no-win", 0, 2, 0, 1);
            AddPlayer(db, profile, "b-open", 0, 8, 0, 1);
            AddPlayer(db, profile, "c-null-answer", 0, 1, 0, 1, winningName: null);
            AddPlayer(db, profile, "d-no-win", 0, 3, 0, 1);
            AddPlayer(db, profile, "e-no-win", 0, 4, 0, 1);
            AddPlayer(db, profile, "f-no-win", 0, 1, 0, 1);
            var open = Round(profile, "incomplete-attempts", 1, true, GuessRoundStatus.Open);
            open.Votes.Add(
                new()
                {
                    Login = "b-open",
                    GuessName = "blue",
                    GuessedAtUtc = _epoch.AddHours(1),
                }
            );
            _ = db.Rounds.Add(open);
            _ = await db.SaveChangesAsync();
        }

        var service = new GuessingHistoryService(fixture);
        var first = await service.LoadLeaderboardAsync(hostId, new() { PageSize = 10 }, default);
        var second = await service.LoadLeaderboardAsync(
            hostId,
            new() { Page = 2, PageSize = 10 },
            default
        );
        var entries = first.Entries.Concat(second.Entries).ToArray();
        entries
            .Select(entry => entry.Login)
            .ShouldBe([
                "c-early",
                "z-tied",
                "b-later",
                "a-lower-rate",
                "better-rate-fewer-wins",
                "incomplete-attempts",
                "a-no-win",
                "b-open",
                "c-null-answer",
                "d-no-win",
                "e-no-win",
                "f-no-win",
            ]);
        first.Entries.Count.ShouldBe(10);
        second.Entries.Count.ShouldBe(2);
        entries.Select(entry => entry.Rank).ShouldBe(Enumerable.Range(1, 12));
        foreach (var page in new[] { first, second })
        {
            page.TotalEntries.ShouldBe(12);
            page.TotalPlayers.ShouldBe(12);
            page.TotalGuesses.ShouldBe(55);
            page.CorrectGuesses.ShouldBe(15);
            page.PageCount.ShouldBe(2);
        }
        entries[0].CorrectGuesses.ShouldBe(3);
        entries[0].RoundsPlayed.ShouldBe(6);
        entries[0].HitRate.ShouldBe(0.5);
        entries[0].FirstWinAtUtc.ShouldBe(_epoch.AddHours(10));
        entries[2].FirstWinAtUtc.ShouldBe(_epoch.AddHours(20));
        entries[4].HitRate.ShouldBe(2.0 / 3);
        entries[5].CorrectGuesses.ShouldBe(1);
        entries[5].RoundsPlayed.ShouldBe(5);
        entries[5].HitRate.ShouldBe(0.2);
        entries[5].FirstWinAtUtc.ShouldBe(_epoch.AddHours(30));
        foreach (var entry in entries.Skip(6))
        {
            entry.CorrectGuesses.ShouldBe(0);
            entry.HitRate.ShouldBe(0);
            entry.FirstWinAtUtc.ShouldBeNull();
        }
        var leader = (await service.LoadPublicLeaderAsync(hostId, default)).ShouldNotBeNull();
        leader.Login.ShouldBe(first.Entries[0].Login);
        leader.CorrectGuesses.ShouldBe(first.Entries[0].CorrectGuesses);
        leader.RoundsPlayed.ShouldBe(first.Entries[0].RoundsPlayed);
        leader.FirstWinAtUtc.ShouldBe(first.Entries[0].FirstWinAtUtc);
    }

    private static async Task FilteredAsync(PointProviderFixture fixture)
    {
        await using var owned = fixture;
        int hostId;
        int profileId;
        await using (var db = fixture.CreateDbContext())
        {
            var profile = await ProfileAsync(db, "filtered");
            var otherProfile = await ProfileAsync(db, "other-profile", profile.HostId);
            var otherHost = await ProfileAsync(db, "other-host");
            hostId = profile.HostId;
            profileId = profile.Id;
            db.Rounds.AddRange(
                Round(profile, "user-alpha", 1, true),
                Round(profile, "user-alpha", 5, false),
                Round(profile, "user-alpha", 6, true),
                Round(profile, "user-alpha", 7, true),
                Round(profile, "user-zeta", 5, true),
                Round(profile, "user-zeta", 6, false),
                Round(otherProfile, "user-alpha", 5, true),
                Round(otherHost, "user-alpha", 5, true),
                Round(otherHost, "user-other-host", 5, true),
                Round(profile, "unrelated", 5, true)
            );
            _ = await db.SaveChangesAsync();
        }

        var service = new GuessingHistoryService(fixture);
        var filtered = await service.LoadLeaderboardAsync(
            hostId,
            new()
            {
                FromUtc = _epoch.AddHours(5),
                ToUtc = _epoch.AddHours(7),
                ProfileId = profileId,
                Username = " USER- ",
            },
            default
        );
        filtered.Entries.Select(entry => entry.Login).ShouldBe(["user-zeta", "user-alpha"]);
        filtered.Entries.Select(entry => entry.Rank).ShouldBe([1, 2]);
        filtered.TotalGuesses.ShouldBe(4);
        filtered.CorrectGuesses.ShouldBe(2);
        filtered.TotalEntries.ShouldBe(2);
        filtered.TotalPlayers.ShouldBe(2);
        filtered.Entries[0].FirstWinAtUtc.ShouldBe(_epoch.AddHours(5));
        filtered.Entries[1].FirstWinAtUtc.ShouldBe(_epoch.AddHours(6));
        filtered.Entries.ShouldAllBe(entry => entry.RoundsPlayed == 2 && entry.HitRate == 0.5);
        filtered
            .Entries.Select(entry => entry.LastGuessAtUtc)
            .ShouldBe([_epoch.AddHours(6), _epoch.AddHours(6)]);
        var allTime = await service.LoadLeaderboardAsync(hostId, new(), default);
        allTime.TotalGuesses.ShouldBe(8);
        allTime.CorrectGuesses.ShouldBe(6);
        allTime.TotalPlayers.ShouldBe(3);
        var leader = (await service.LoadPublicLeaderAsync(hostId, default)).ShouldNotBeNull();
        leader.Login.ShouldBe("user-alpha");
        leader.Login.ShouldBe(allTime.Entries[0].Login);
        leader.FirstWinAtUtc.ShouldBe(_epoch.AddHours(1));
        leader.CorrectGuesses.ShouldBe(4);
        leader.RoundsPlayed.ShouldBe(5);
    }

    private static async Task<GuessRoundProfile> ProfileAsync(
        BlokeBotDbContext db,
        string login,
        int? hostId = null
    )
    {
        if (hostId is null)
        {
            var host = new BotHost
            {
                Login = login,
                DisplayName = login,
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = _epoch,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }
        var profile = new GuessRoundProfile
        {
            HostId = hostId.Value,
            Name = login,
            Slug = login,
            ReplySettings = new(),
        };
        _ = db.Profiles.Add(profile);
        _ = await db.SaveChangesAsync();
        return profile;
    }

    private static void AddPlayer(
        BlokeBotDbContext db,
        GuessRoundProfile profile,
        string login,
        int wins,
        int attempts,
        int firstWinHour,
        int firstLossHour,
        string? winningName = "blue"
    )
    {
        for (var i = 0; i < wins; i++)
        {
            _ = db.Rounds.Add(Round(profile, login, firstWinHour + i, true));
        }
        for (var i = 0; i < attempts - wins; i++)
        {
            _ = db.Rounds.Add(
                Round(profile, login, firstLossHour + i, false, winningName: winningName)
            );
        }
    }

    private static GuessRound Round(
        GuessRoundProfile profile,
        string login,
        int hour,
        bool correct,
        GuessRoundStatus status = GuessRoundStatus.Completed,
        string? winningName = "blue"
    ) =>
        new()
        {
            HostId = profile.HostId,
            GuessRoundProfileId = profile.Id,
            Status = status,
            StartedAtUtc = _epoch,
            ClosedAtUtc = status == GuessRoundStatus.Open ? null : _epoch.AddDays(2),
            WinningName = winningName,
            Votes =
            [
                new()
                {
                    Login = login,
                    GuessName = correct ? "blue" : "red",
                    GuessedAtUtc = _epoch.AddHours(hour),
                },
            ],
        };
}
