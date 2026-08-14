using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPassportServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Save_UsesTwitchIdentityAcrossRenamesAndRejectsUnearnedRewards()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHost = await SeedHostAsync(database, "first", HostFeatureFlags.ViewerPassports);
        var secondHost = await SeedHostAsync(database, "second", HostFeatureFlags.ViewerPassports);
        var titleId = await SeedEarnedTitleAsync(database, firstHost, "viewer-id");
        var service = CreateService(database);

        var first = await service.SaveAsync(
            Save(firstHost, "viewer-id", "old_login") with
            {
                SelectedTitleRewardId = titleId,
            },
            default
        );
        var renamed = await service.SaveAsync(
            Save(firstHost, "viewer-id", "new_login") with
            {
                SelectedTitleRewardId = titleId,
            },
            default
        );
        var otherHost = await service.SaveAsync(
            Save(secondHost, "viewer-id", "new_login"),
            default
        );
        var unearned = await service.SaveAsync(
            Save(firstHost, "other-id", "other") with
            {
                SelectedTitleRewardId = titleId,
            },
            default
        );
        var overlong = await service.SaveAsync(
            Save(firstHost, "viewer-id", "new_login") with
            {
                ProfileLine = new string('x', ViewerPassportLimits.ProfileLineMaximumLength + 1),
            },
            default
        );

        _ = first.ShouldBeOfType<ViewerPassportMutationOutcome.Succeeded>();
        var current = renamed.ShouldBeOfType<ViewerPassportMutationOutcome.Succeeded>().Passport;
        current.Login.ShouldBe("new_login");
        current.SelectedTitle.ShouldNotBeNull().Id.ShouldBe(titleId);
        _ = otherHost.ShouldBeOfType<ViewerPassportMutationOutcome.Succeeded>();
        _ = unearned.ShouldBeOfType<ViewerPassportMutationOutcome.UnearnedReward>();
        _ = overlong.ShouldBeOfType<ViewerPassportMutationOutcome.Invalid>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.ViewerPassports.CountAsync()).ShouldBe(2);
        (
            await verify.ViewerPassports.CountAsync(value =>
                value.HostId == firstHost && value.TwitchUserId == "viewer-id"
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task PrivateRename_HidesRememberedLoginsFromPublicLegacyLeaderboards()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var service = CreateService(database);
        _ = Success(await service.SaveAsync(Save(hostId, "viewer-id", "old_login"), default));
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "old_login",
                    Amount = "125",
                    UpdatedAtUtc = _now.UtcDateTime,
                }
            );
            SeedCompletedGuess(seed, hostId, "old_login");
            _ = await seed.SaveChangesAsync();
        }
        await using (var inspect = await database.CreateDbContextAsync())
        {
            var vote = await inspect.Votes.Include(value => value.GuessRound).SingleAsync();
            vote.GuessName.ShouldBe("blue");
            vote.GuessRound.ShouldNotBeNull().WinningName.ShouldBe("blue");
        }

        var renamed = Success(
            await service.SaveAsync(Save(hostId, "viewer-id", "new_login"), default)
        );
        var exclusions = await new ViewerPassportPublicIdentityPolicy(database).ExclusionsAsync(
            hostId,
            default
        );

        exclusions.Logins.Order().ShouldBe(["new_login", "old_login"]);
        (
            await new PointBalanceService(database).GetPublicLeaderboardAsync(
                hostId,
                10,
                exclusions.Logins,
                default
            )
        ).ShouldBeEmpty();
        (
            await new GuessingHistoryService(database).LoadPublicLeaderboardAsync(
                hostId,
                new GuessHistoryQuery { Page = 1, PageSize = 10 },
                exclusions.Logins,
                default
            )
        ).Entries.ShouldBeEmpty();
        renamed.Statistics.Points.ShouldBe("125");
        renamed.Statistics.GuessRounds.ShouldBe(1);
        renamed.Statistics.CorrectGuesses.ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify
                .ViewerPassportLogins.OrderBy(value => value.Login)
                .Select(value => value.Login)
                .ToArrayAsync()
        ).ShouldBe(["new_login", "old_login"]);
    }

    [Test]
    public async Task GamesWon_CountsDistinctBingoGamesAndNotCorrectGuesses()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var service = CreateService(database);
        _ = Success(await service.SaveAsync(Save(hostId, "viewer-id", "viewer"), default));
        await using (var seed = await database.CreateDbContextAsync())
        {
            SeedCompletedGuess(seed, hostId, "viewer");
            _ = await seed.SaveChangesAsync();
        }
        await using (var inspect = await database.CreateDbContextAsync())
        {
            var vote = await inspect.Votes.Include(value => value.GuessRound).SingleAsync();
            string.Equals(
                    vote.GuessName,
                    vote.GuessRound.ShouldNotBeNull().WinningName,
                    StringComparison.OrdinalIgnoreCase
                )
                .ShouldBeTrue();
        }

        var beforeWin = (
            await service.GetSelfAsync(hostId, new("viewer-id", "viewer", "Viewer"), default)
        )
            .ShouldBeOfType<ViewerPassportQueryOutcome.Available>()
            .Passport.Statistics;
        beforeWin.CorrectGuesses.ShouldBe(1);
        beforeWin.GamesWon.ShouldBe(0);

        await SeedBingoWinsAsync(database, hostId, "viewer-id");
        await using (var inspect = await database.CreateDbContextAsync())
        {
            (await inspect.BingoWinRecipients.CountAsync()).ShouldBe(2);
            (
                await (
                    from recipient in inspect.BingoWinRecipients
                    join win in inspect.BingoWins on recipient.WinId equals win.Id
                    where
                        recipient.HostId == hostId
                        && recipient.TwitchUserId == "viewer-id"
                        && win.HostId == hostId
                    select win.GameId
                )
                    .Distinct()
                    .CountAsync()
            ).ShouldBe(1);
        }

        var afterWin = (
            await service.GetSelfAsync(hostId, new("viewer-id", "viewer", "Viewer"), default)
        )
            .ShouldBeOfType<ViewerPassportQueryOutcome.Available>()
            .Passport.Statistics;
        afterWin.CorrectGuesses.ShouldBe(1);
        afterWin.GamesWon.ShouldBe(1);
    }

    [Test]
    public async Task Visibility_SeparatesAnonymousMembersOwnerAndPublicProjections()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var service = CreateService(database);
        var projections = new ViewerPassportProjectionService(service);
        _ = Success(
            await service.SaveAsync(
                Save(hostId, "subject-id", "subject") with
                {
                    Visibility = ViewerPassportVisibility.ChannelMembers,
                    ProfileLine = "MEMBERS-ONLY-LINE",
                    HideAttendance = false,
                },
                default
            )
        );
        _ = Success(await service.SaveAsync(Save(hostId, "member-id", "member"), default));
        _ = await service.RecordStreamAttendanceAsync(
            "channel",
            new("subject-id", "subject", "Subject"),
            _now,
            default
        );
        await using (var seed = await database.CreateDbContextAsync())
        {
            seed.PointBalances.AddRange(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "subject",
                    Amount = "100",
                    UpdatedAtUtc = _now.UtcDateTime,
                },
                new PointBalance
                {
                    HostId = hostId,
                    Login = "member",
                    Amount = "50",
                    UpdatedAtUtc = _now.UtcDateTime,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        var anonymous = await service.GetVisibleAsync(
            "channel",
            "subject",
            ViewerPassportAudience.Anonymous,
            default
        );
        var member = await service.GetVisibleAsync(
            "channel",
            "subject",
            new("member-id", false),
            default
        );
        var owner = await service.GetVisibleAsync(
            "channel",
            "subject",
            new("subject-id", false),
            default
        );

        _ = anonymous.ShouldBeOfType<ViewerPassportQueryOutcome.Forbidden>();
        member
            .ShouldBeOfType<ViewerPassportQueryOutcome.Available>()
            .Passport.ProfileLine.ShouldBe("MEMBERS-ONLY-LINE");
        _ = owner.ShouldBeOfType<ViewerPassportQueryOutcome.Available>();
        (await projections.GetOverlayDataAsync("channel", "subject", default)).ShouldBeNull();
        (await projections.GetAutomationPayloadAsync("channel", "subject", default)).ShouldBeNull();
        var hidden = await new ViewerPassportPublicIdentityPolicy(database).ExclusionsAsync(
            hostId,
            default
        );
        (
            await new PointBalanceService(database).GetPublicLeaderboardAsync(
                hostId,
                10,
                hidden.Logins,
                default
            )
        ).ShouldBeEmpty();

        _ = Success(
            await service.SaveAsync(
                Save(hostId, "subject-id", "subject") with
                {
                    Visibility = ViewerPassportVisibility.Public,
                    ProfileLine = "PUBLIC-LINE",
                    HideAttendance = true,
                },
                default
            )
        );
        var overlay = await projections.GetOverlayDataAsync("channel", "subject", default);
        overlay.ShouldNotBeNull().ProfileLine.ShouldBe("PUBLIC-LINE");
        overlay.AttendanceStreakSessions.ShouldBeNull();
        (await projections.GetAutomationPayloadAsync("channel", "subject", default))
            .ShouldNotBeNull()
            .AttendanceStreakSessions.ShouldBeNull();
        var visible = await new ViewerPassportPublicIdentityPolicy(database).ExclusionsAsync(
            hostId,
            default
        );
        (
            await new PointBalanceService(database).GetPublicLeaderboardAsync(
                hostId,
                10,
                visible.Logins,
                default
            )
        )
            .Select(value => value.Login)
            .ShouldBe(["subject"]);
    }

    [Test]
    public async Task RepeatedChatInOneStream_RecordsAttendanceOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var streams = new MutableStreamLivenessProvider("stream-1", _now.AddHours(-1));
        var service = CreateService(database, streams);
        var viewer = new ViewerPassportIdentity("viewer-id", "viewer", "Viewer");

        (
            await service.RecordStreamAttendanceAsync("channel", viewer, _now, default)
        ).ShouldBeTrue();
        (
            await service.RecordStreamAttendanceAsync("channel", viewer, _now.AddHours(2), default)
        ).ShouldBeFalse();

        await using var verify = await database.CreateDbContextAsync();
        (await verify.ViewerPassportStreamSessions.CountAsync()).ShouldBe(1);
        (await verify.ViewerPassportStreamAttendances.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task AdjacentRecordedStreams_IncrementAttendanceStreak()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var streams = new MutableStreamLivenessProvider("stream-1", _now.AddHours(-2));
        var service = CreateService(database, streams);
        var viewer = new ViewerPassportIdentity("viewer-id", "viewer", "Viewer");
        _ = await service.RecordStreamAttendanceAsync("channel", viewer, _now, default);
        streams.Set("stream-2", _now.AddHours(-1));

        _ = await service.RecordStreamAttendanceAsync("channel", viewer, _now, default);

        var passport = (await service.GetSelfAsync(hostId, viewer, default))
            .ShouldBeOfType<ViewerPassportQueryOutcome.Available>()
            .Passport;
        passport.Statistics.AttendanceStreakSessions.ShouldBe(2);
    }

    [Test]
    public async Task RecordedInterveningStream_ResetsNextAttendanceStreak()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var streams = new MutableStreamLivenessProvider("stream-1", _now.AddHours(-3));
        var service = CreateService(database, streams);
        var viewer = new ViewerPassportIdentity("viewer-id", "viewer", "Viewer");
        _ = await service.RecordStreamAttendanceAsync("channel", viewer, _now, default);
        streams.Set("stream-2", _now.AddHours(-2));
        _ = await service.RecordStreamAttendanceAsync(
            "channel",
            new("other-id", "other", "Other"),
            _now,
            default
        );
        streams.Set("stream-3", _now.AddHours(-1));

        _ = await service.RecordStreamAttendanceAsync("channel", viewer, _now, default);

        var passport = (await service.GetSelfAsync(hostId, viewer, default))
            .ShouldBeOfType<ViewerPassportQueryOutcome.Available>()
            .Passport;
        passport.Statistics.AttendanceStreakSessions.ShouldBe(1);
    }

    [Test]
    public async Task ExportAndReset_AreHostScopedAndLeaveSourceHistoryIntact()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var viewer = new ViewerPassportIdentity("viewer-id", "viewer", "Viewer");
        var streams = new MutableStreamLivenessProvider("stream-1", _now.AddHours(-1));
        var service = CreateService(database, streams);
        _ = Success(await service.SaveAsync(Save(hostId, "viewer-id", "viewer"), default));
        _ = await service.RecordStreamAttendanceAsync("channel", viewer, _now, default);
        _ = await service.RecordStreamAttendanceAsync(
            "channel",
            new("other-id", "other", "Other"),
            _now,
            default
        );
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "viewer",
                    Amount = "42",
                    UpdatedAtUtc = _now.UtcDateTime,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        var export = (
            await service.ExportAsync(hostId, viewer, default)
        ).ShouldBeOfType<ViewerPassportExportOutcome.Succeeded>();
        export.Sections.Keys.ShouldContain("viewer-passports.profiles");
        export.Sections.Keys.ShouldContain("viewer-passports.logins");
        export.Sections["viewer-passports.stream-attendance"].Count.ShouldBe(1);
        _ = (
            await service.ResetAsync(hostId, "viewer-id", default)
        ).ShouldBeOfType<ViewerPassportResetOutcome.Succeeded>();

        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.ViewerPassports.CountAsync(value => value.TwitchUserId == "viewer-id")
        ).ShouldBe(0);
        (
            await verify.ViewerPassportLogins.CountAsync(value =>
                value.Passport!.TwitchUserId == "viewer-id"
            )
        ).ShouldBe(0);
        (await verify.ViewerPassportStreamAttendances.CountAsync()).ShouldBe(1);
        (await verify.ViewerPassportStreamSessions.CountAsync()).ShouldBe(1);
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("42");
    }

    [Test]
    public async Task ReusedLogin_FailsClosedAcrossPublicStatsPrivacyErasureAndReset()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var service = CreateService(database);
        var firstIdentity = new ViewerPassportIdentity("first-id", "shared", "First");
        var secondIdentity = new ViewerPassportIdentity("second-id", "shared", "Second");
        _ = Success(await service.SaveAsync(Save(hostId, "first-id", "shared"), default));
        _ = await service.RecordStreamAttendanceAsync("channel", firstIdentity, _now, default);
        await SeedLegacyActivityAsync(database, hostId, "shared", "75");
        _ = Success(
            await service.SaveAsync(
                Save(hostId, "second-id", "shared") with
                {
                    Visibility = ViewerPassportVisibility.Public,
                },
                default
            )
        );
        _ = await service.RecordStreamAttendanceAsync("channel", secondIdentity, _now, default);

        var first = (
            await service.GetVisibleByIdentityAsync(
                "channel",
                firstIdentity,
                new("first-id", false),
                default
            )
        )
            .ShouldBeOfType<ViewerPassportQueryOutcome.Available>()
            .Passport;
        var second = (
            await service.GetVisibleByIdentityAsync(
                "channel",
                secondIdentity,
                new("second-id", false),
                default
            )
        )
            .ShouldBeOfType<ViewerPassportQueryOutcome.Available>()
            .Passport;
        var publicProfile = await service.GetVisibleAsync(
            "channel",
            "shared",
            ViewerPassportAudience.Anonymous,
            default
        );

        first.TwitchUserId.ShouldBe("first-id");
        second.TwitchUserId.ShouldBe("second-id");
        _ = publicProfile.ShouldBeOfType<ViewerPassportQueryOutcome.NotFound>();
        foreach (var statistics in new[] { first.Statistics, second.Statistics })
        {
            statistics.Points.ShouldBe("0");
            statistics.PointsRank.ShouldBeNull();
            statistics.GuessRounds.ShouldBe(0);
            statistics.CorrectGuesses.ShouldBe(0);
            statistics.GiveawaysWon.ShouldBe(0);
        }

        var exclusions = await new ViewerPassportPublicIdentityPolicy(database).ExclusionsAsync(
            hostId,
            default
        );
        exclusions.Logins.ShouldContain("shared");
        (
            await new PointBalanceService(database).GetPublicLeaderboardAsync(
                hostId,
                10,
                exclusions.Logins,
                default
            )
        ).ShouldBeEmpty();
        (
            await new GuessingHistoryService(database).LoadPublicLeaderboardAsync(
                hostId,
                new GuessHistoryQuery { Page = 1, PageSize = 10 },
                exclusions.Logins,
                default
            )
        ).Entries.ShouldBeEmpty();

        foreach (var identity in new[] { firstIdentity, secondIdentity })
        {
            var export = (
                await service.ExportAsync(hostId, identity, default)
            ).ShouldBeOfType<ViewerPassportExportOutcome.Succeeded>();
            export.Sections.ShouldNotContainKey("points.balances");
            export.Sections.ShouldNotContainKey("guessing.votes");
            export.Sections.ShouldNotContainKey("points.giveaway-entries");
            export.Sections.ShouldNotContainKey("points.giveaway-wins");
            export
                .Sections["viewer-passports.profiles"]
                .Cast<ViewerPassport>()
                .Single()
                .TwitchUserId.ShouldBe(identity.TwitchUserId);
            export.Sections["viewer-passports.stream-attendance"].Count.ShouldBe(1);
        }
        await using (var loginOnly = await database.CreateDbContextAsync())
        {
            var export = await ViewerPrivacyService.ExportAsync(
                loginOnly,
                PrivacySubject.Create(null, "shared"),
                hostId,
                default
            );
            export.Sections.ShouldNotContainKey("viewer-passports.profiles");
            export.Sections.ShouldNotContainKey("points.balances");
            export.Sections.ShouldNotContainKey("guessing.votes");
            export.Sections.ShouldNotContainKey("points.giveaway-wins");
        }
        await using (var stableOnly = await database.CreateDbContextAsync())
        {
            var export = await ViewerPrivacyService.ExportAsync(
                stableOnly,
                PrivacySubject.Create("first-id", null),
                hostId,
                default
            );
            export
                .Sections["viewer-passports.profiles"]
                .Cast<ViewerPassport>()
                .Single()
                .TwitchUserId.ShouldBe("first-id");
            export.Sections.ShouldNotContainKey("points.balances");
        }

        await using (var eraseFirst = await database.CreateDbContextAsync())
        {
            var report = await ViewerPrivacyService.EraseAsync(
                eraseFirst,
                PrivacySubject.Create("first-id", null),
                hostId,
                default
            );
            report.ChangedRows["viewer-passports.profiles"].ShouldBe(1);
            report.ChangedRows["viewer-passports.stream-attendance"].ShouldBe(1);
            report.ChangedRows.ShouldNotContainKey("points.balances");
            report.ChangedRows.ShouldNotContainKey("guessing.votes");
            report.ChangedRows.ShouldNotContainKey("points.giveaway-wins");
        }
        _ = (
            await service.ResetAsync(hostId, "second-id", default)
        ).ShouldBeOfType<ViewerPassportResetOutcome.Succeeded>();
        await SetFeaturesAsync(database, hostId, HostFeatureFlags.None);

        var sticky = await new ViewerPassportPublicIdentityPolicy(database).ExclusionsAsync(
            hostId,
            default
        );
        sticky.Logins.ShouldContain("shared");
        await using (var eraseLogin = await database.CreateDbContextAsync())
        {
            var report = await ViewerPrivacyService.EraseAsync(
                eraseLogin,
                PrivacySubject.Create(null, "shared"),
                hostId,
                default
            );
            report.TotalChangedRows.ShouldBe(0);
        }
        await using var verify = await database.CreateDbContextAsync();
        (await verify.ViewerPassports.CountAsync()).ShouldBe(0);
        (await verify.ViewerPassportStreamAttendances.CountAsync()).ShouldBe(0);
        (await verify.ViewerPassportStreamSessions.CountAsync()).ShouldBe(1);
        (await verify.ViewerPassportAmbiguousLogins.CountAsync()).ShouldBe(1);
        (await verify.PointBalances.CountAsync(value => value.Login == "shared")).ShouldBe(1);
        (await verify.Votes.CountAsync(value => value.Login == "shared")).ShouldBe(1);
        (await verify.PointsGiveawayEntrants.CountAsync(value => value.Login == "shared")).ShouldBe(
            1
        );
        (await verify.PointsGiveawayWinners.CountAsync(value => value.Login == "shared")).ShouldBe(
            1
        );
    }

    [Test]
    public async Task UniqueAlias_RemainsAttributedWithinItsHost()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var ambiguousHost = await SeedHostAsync(
            database,
            "ambiguous",
            HostFeatureFlags.ViewerPassports
        );
        var uniqueHost = await SeedHostAsync(database, "unique", HostFeatureFlags.ViewerPassports);
        var service = CreateService(database);
        _ = Success(await service.SaveAsync(Save(ambiguousHost, "first-id", "old_name"), default));
        _ = await service.RecordStreamAttendanceAsync(
            "ambiguous",
            new("first-id", "old_name", "First"),
            _now,
            default
        );
        _ = Success(await service.SaveAsync(Save(ambiguousHost, "second-id", "old_name"), default));
        _ = Success(await service.SaveAsync(Save(uniqueHost, "owner-id", "old_name"), default));
        await SeedLegacyActivityAsync(database, uniqueHost, "old_name", "90");
        var renamed = Success(
            await service.SaveAsync(
                Save(uniqueHost, "owner-id", "new_name") with
                {
                    Visibility = ViewerPassportVisibility.Public,
                },
                default
            )
        );
        _ = await service.RecordStreamAttendanceAsync(
            "unique",
            new("owner-id", "new_name", "Owner"),
            _now,
            default
        );

        renamed.Statistics.Points.ShouldBe("90");
        renamed.Statistics.GuessRounds.ShouldBe(1);
        renamed.Statistics.CorrectGuesses.ShouldBe(1);
        renamed.Statistics.GiveawaysWon.ShouldBe(1);
        var exclusions = await new ViewerPassportPublicIdentityPolicy(database).ExclusionsAsync(
            uniqueHost,
            default
        );
        exclusions.Logins.ShouldNotContain("old_name");
        (
            await new PointBalanceService(database).GetPublicLeaderboardAsync(
                uniqueHost,
                10,
                exclusions.Logins,
                default
            )
        )
            .Single()
            .Login.ShouldBe("old_name");

        var stableExport = (
            await service.ExportAsync(uniqueHost, new("owner-id", "new_name", "Owner"), default)
        ).ShouldBeOfType<ViewerPassportExportOutcome.Succeeded>();
        stableExport.Sections.ShouldContainKey("points.balances");
        stableExport.Sections.ShouldContainKey("guessing.votes");
        stableExport.Sections.ShouldContainKey("points.giveaway-wins");
        stableExport.Sections["viewer-passports.stream-attendance"].Count.ShouldBe(1);
        await using (var loginOnly = await database.CreateDbContextAsync())
        {
            var export = await ViewerPrivacyService.ExportAsync(
                loginOnly,
                PrivacySubject.Create(null, "old_name"),
                uniqueHost,
                default
            );
            export
                .Sections["viewer-passports.profiles"]
                .Cast<ViewerPassport>()
                .Single()
                .TwitchUserId.ShouldBe("owner-id");
            export.Sections.ShouldContainKey("points.balances");
        }

        await using (var erase = await database.CreateDbContextAsync())
        {
            var report = await ViewerPrivacyService.EraseAsync(
                erase,
                PrivacySubject.Create("owner-id", "new_name"),
                uniqueHost,
                default
            );
            report.ChangedRows["viewer-passports.profiles"].ShouldBe(1);
            report.ChangedRows["viewer-passports.stream-attendance"].ShouldBe(1);
            report.ChangedRows["points.balances"].ShouldBe(1);
            report.ChangedRows["guessing.votes"].ShouldBe(1);
            report.ChangedRows["points.giveaway-entries"].ShouldBe(1);
            report.ChangedRows["points.giveaway-wins"].ShouldBe(1);
        }
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify
                .ViewerPassportAmbiguousLogins.OrderBy(value => value.HostId)
                .ThenBy(value => value.Login)
                .Select(value => new { value.HostId, value.Login })
                .ToArrayAsync()
        ).ShouldBe([
            new { HostId = ambiguousHost, Login = "old_name" },
            new { HostId = uniqueHost, Login = "new_name" },
            new { HostId = uniqueHost, Login = "old_name" },
        ]);
        (await verify.PointBalances.CountAsync(value => value.HostId == uniqueHost)).ShouldBe(0);
        (await verify.ViewerPassportStreamAttendances.CountAsync()).ShouldBe(1);
        (await verify.ViewerPassportStreamSessions.CountAsync()).ShouldBe(2);
        (await verify.Votes.CountAsync(value => value.GuessRound!.HostId == uniqueHost)).ShouldBe(
            0
        );
        (
            await verify.PointsGiveawayEntrants.CountAsync(value =>
                value.Giveaway!.HostId == uniqueHost
            )
        ).ShouldBe(0);
        (
            await verify.PointsGiveawayWinners.SingleAsync(value =>
                value.Giveaway!.HostId == uniqueHost
            )
        ).Login.ShouldBe(ViewerPrivacyService.ErasedToken);
    }

    [Test]
    public async Task ConcurrentFirstMessages_RecordOneSessionAndBothAttendanceRows()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var streams = new GatedStreamLivenessProvider(
            new HostStreamLivenessOutcome.Live("stream-1", _now.AddHours(-1))
        );
        var service = CreateService(database, streams);

        var first = service.RecordStreamAttendanceAsync(
            "channel",
            new("first-id", "shared", "First"),
            _now,
            default
        );
        var second = service.RecordStreamAttendanceAsync(
            "channel",
            new("second-id", "shared", "Second"),
            _now,
            default
        );
        await streams.BothCallsArrived;
        streams.Release();
        var results = await Task.WhenAll(first, second);

        results.ShouldAllBe(value => value);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.ViewerPassportStreamSessions.CountAsync()).ShouldBe(1);
        (await verify.ViewerPassportStreamAttendances.CountAsync()).ShouldBe(2);
        (await verify.ViewerPassportAmbiguousLogins.CountAsync()).ShouldBe(1);
        (await verify.ViewerPassportLogins.CountAsync(value => value.Login == "shared")).ShouldBe(
            2
        );
        (
            await verify
                .ViewerPassportLogins.Where(value => value.Login == "shared")
                .Select(value => value.Passport!.TwitchUserId)
                .Order()
                .ToArrayAsync()
        ).ShouldBe(["first-id", "second-id"]);
    }

    [Test]
    public async Task ResetBeforeReuse_TombstonesAliasesAndKeepsHostsIsolated()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHost = await SeedHostAsync(database, "first", HostFeatureFlags.ViewerPassports);
        var secondHost = await SeedHostAsync(database, "second", HostFeatureFlags.ViewerPassports);
        var service = CreateService(database);
        _ = Success(await service.SaveAsync(Save(firstHost, "owner-id", "old_name"), default));
        await SeedLegacyActivityAsync(database, firstHost, "old_name", "75");
        _ = Success(await service.SaveAsync(Save(firstHost, "owner-id", "new_name"), default));

        _ = (
            await service.ResetAsync(firstHost, "owner-id", default)
        ).ShouldBeOfType<ViewerPassportResetOutcome.Succeeded>();
        await using (var inspect = await database.CreateDbContextAsync())
        {
            (
                await inspect
                    .ViewerPassportAmbiguousLogins.OrderBy(value => value.Login)
                    .Select(value => value.Login)
                    .ToArrayAsync()
            ).ShouldBe(["new_name", "old_name"]);
        }

        var recreated = Success(
            await service.SaveAsync(
                Save(firstHost, "owner-id", "old_name") with
                {
                    Visibility = ViewerPassportVisibility.Public,
                },
                default
            )
        );
        recreated.Statistics.Points.ShouldBe("0");
        recreated.Statistics.GuessRounds.ShouldBe(0);
        recreated.Statistics.GiveawaysWon.ShouldBe(0);

        var reused = Success(
            await service.SaveAsync(
                Save(firstHost, "next-id", "old_name") with
                {
                    Visibility = ViewerPassportVisibility.Public,
                },
                default
            )
        );
        reused.Statistics.Points.ShouldBe("0");
        reused.Statistics.GuessRounds.ShouldBe(0);
        reused.Statistics.GiveawaysWon.ShouldBe(0);
        _ = (
            await service.GetVisibleAsync(
                "first",
                "old_name",
                ViewerPassportAudience.Anonymous,
                default
            )
        ).ShouldBeOfType<ViewerPassportQueryOutcome.NotFound>();
        var exclusions = await new ViewerPassportPublicIdentityPolicy(database).ExclusionsAsync(
            firstHost,
            default
        );
        exclusions.Logins.ShouldContain("old_name");
        exclusions.Logins.ShouldContain("new_name");
        (
            await new PointBalanceService(database).GetPublicLeaderboardAsync(
                firstHost,
                10,
                exclusions.Logins,
                default
            )
        ).ShouldBeEmpty();

        _ = Success(
            await service.SaveAsync(
                Save(secondHost, "isolated-id", "old_name") with
                {
                    Visibility = ViewerPassportVisibility.Public,
                },
                default
            )
        );
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = secondHost,
                    Login = "old_name",
                    Amount = "12",
                    UpdatedAtUtc = _now.UtcDateTime,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        (
            await service.GetVisibleAsync(
                "second",
                "old_name",
                ViewerPassportAudience.Anonymous,
                default
            )
        )
            .ShouldBeOfType<ViewerPassportQueryOutcome.Available>()
            .Passport.Statistics.Points.ShouldBe("12");
    }

    [Test]
    public async Task ErasureBeforeReuse_TombstonesTheLoginAndDetachesFutureHistory()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var service = CreateService(database);
        _ = Success(await service.SaveAsync(Save(hostId, "owner-id", "reused_name"), default));
        _ = await service.RecordStreamAttendanceAsync(
            "channel",
            new("owner-id", "reused_name", "Owner"),
            _now,
            default
        );
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "reused_name",
                    Amount = "25",
                    UpdatedAtUtc = _now.UtcDateTime,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        await using (var erase = await database.CreateDbContextAsync())
        {
            var report = await ViewerPrivacyService.EraseAsync(
                erase,
                PrivacySubject.Create("owner-id", "caller_supplied_mismatch"),
                hostId,
                default
            );
            report.ChangedRows["points.balances"].ShouldBe(1);
            report.ChangedRows["viewer-passports.profiles"].ShouldBe(1);
            report.ChangedRows["viewer-passports.stream-attendance"].ShouldBe(1);
        }
        await using (var laterHistory = await database.CreateDbContextAsync())
        {
            _ = laterHistory.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "reused_name",
                    Amount = "40",
                    UpdatedAtUtc = _now.AddMinutes(1).UtcDateTime,
                }
            );
            _ = await laterHistory.SaveChangesAsync();
        }

        var nextOwner = Success(
            await service.SaveAsync(
                Save(hostId, "next-id", "reused_name") with
                {
                    Visibility = ViewerPassportVisibility.Public,
                },
                default
            )
        );
        nextOwner.Statistics.Points.ShouldBe("0");
        var exclusions = await new ViewerPassportPublicIdentityPolicy(database).ExclusionsAsync(
            hostId,
            default
        );
        exclusions.Logins.ShouldContain("reused_name");
        (
            await new PointBalanceService(database).GetPublicLeaderboardAsync(
                hostId,
                10,
                exclusions.Logins,
                default
            )
        ).ShouldBeEmpty();
    }

    private static ViewerPassportService CreateService(
        SqliteBlokeBotDbFactory database,
        IHostStreamLivenessProvider? streams = null
    ) =>
        new(
            database,
            new PointBalanceService(database),
            streams ?? new MutableStreamLivenessProvider("stream-1", _now.AddHours(-1)),
            new FixedTimeProvider(_now)
        );

    private static SaveViewerPassportCommand Save(int hostId, string userId, string login) =>
        new(
            hostId,
            new(userId, login, login),
            "Profile line",
            ViewerPassportVisibility.Private,
            true,
            null,
            null
        );

    private static ViewerPassportView Success(ViewerPassportMutationOutcome outcome) =>
        outcome.ShouldBeOfType<ViewerPassportMutationOutcome.Succeeded>().Passport;

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory database,
        string login,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = features,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<long> SeedEarnedTitleAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        string twitchUserId
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var season = new CommunitySeason
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            CreationOperationId = Guid.NewGuid(),
            Name = "Season",
            Status = CommunitySeasonStatus.Open,
            Visibility = CommunityVisibility.Hidden,
            StartsAtUtc = _now.AddDays(-1).UtcDateTime,
            EndsAtUtc = _now.AddDays(1).UtcDateTime,
            CreatedAtUtc = _now.UtcDateTime,
            UpdatedAtUtc = _now.UtcDateTime,
        };
        var definition = new CommunityDefinition
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            Season = season,
            Key = "achievement",
            Name = "Achievement",
            Kind = CommunityDefinitionKind.Achievement,
            Scope = CommunityProgressScope.Viewer,
            CompletionMode = CommunityCompletionMode.OneTime,
            EventRule = CommunityEventRuleKind.ExternalGrant,
            Increment = CommunityProgressIncrement.Occurrence,
            Target = 1,
            CreatedAtUtc = _now.UtcDateTime,
        };
        var reward = new CommunityRewardDefinition
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            Season = season,
            Key = "earned-title",
            Kind = CommunityRewardKind.Title,
            Name = "Earned title",
            PresentationToken = "earned-title",
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.CommunitySeasons.Add(season);
        _ = db.CommunityDefinitions.Add(definition);
        _ = db.CommunityRewardDefinitions.Add(reward);
        _ = await db.SaveChangesAsync();
        var completion = new CommunityCompletion
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            SeasonId = season.Id,
            DefinitionId = definition.Id,
            SubjectKey = $"viewer:{twitchUserId}",
            ViewerTwitchUserId = twitchUserId,
            ViewerLogin = "viewer",
            ViewerDisplayName = "Viewer",
            DefinitionKey = definition.Key,
            DefinitionName = definition.Name,
            Sequence = 1,
            SourceOperationKey = Guid.NewGuid().ToString(),
            CompletedAtUtc = _now.UtcDateTime,
        };
        _ = db.CommunityCompletions.Add(completion);
        _ = await db.SaveChangesAsync();
        _ = db.CommunityRewardUnlocks.Add(
            new CommunityRewardUnlock
            {
                HostId = hostId,
                RewardDefinitionId = reward.Id,
                ViewerTwitchUserId = twitchUserId,
                ViewerLogin = "viewer",
                ViewerDisplayName = "Viewer",
                CompletionId = completion.Id,
                GrantedAtUtc = _now.UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();
        return reward.Id;
    }

    private static void SeedCompletedGuess(BlokeBotDbContext db, int hostId, string login)
    {
        var profile = new GuessRoundProfile
        {
            HostId = hostId,
            Name = $"Guess {Guid.NewGuid():N}",
            Slug = $"guess-{Guid.NewGuid():N}",
            ReplySettings = new BotReplySettings(),
        };
        _ = db.Rounds.Add(
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfile = profile,
                Status = GuessRoundStatus.Completed,
                StartedAtUtc = _now.AddMinutes(-5).UtcDateTime,
                ClosedAtUtc = _now.UtcDateTime,
                WinningName = "blue",
                Votes =
                [
                    new GuessVote
                    {
                        Login = login,
                        GuessName = "blue",
                        GuessedAtUtc = _now.AddMinutes(-2).UtcDateTime,
                    },
                ],
            }
        );
    }

    private static async Task SeedLegacyActivityAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        string login,
        string points
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.PointBalances.Add(
            new PointBalance
            {
                HostId = hostId,
                Login = login,
                Amount = points,
                UpdatedAtUtc = _now.UtcDateTime,
            }
        );
        SeedCompletedGuess(db, hostId, login);
        _ = db.PointsGiveaways.Add(
            new PointsGiveaway
            {
                HostId = hostId,
                Status = PointsGiveawayStatus.Completed,
                StartedAtUtc = _now.AddMinutes(-10).UtcDateTime,
                EndsAtUtc = _now.AddMinutes(-5).UtcDateTime,
                CompletedAtUtc = _now.UtcDateTime,
                Entrants =
                [
                    new PointsGiveawayEntrant { Login = login, JoinedAtUtc = _now.UtcDateTime },
                ],
                Winners = [new PointsGiveawayWinner { Login = login, Payout = "25" }],
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static async Task SeedBingoWinsAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        string twitchUserId
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var template = new BingoTemplate
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            CreationOperationId = Guid.NewGuid(),
            Name = "Passport games won",
            CurrentRevision = 1,
            CreatedAtUtc = _now.UtcDateTime,
            UpdatedAtUtc = _now.UtcDateTime,
        };
        _ = db.BingoTemplates.Add(template);
        _ = await db.SaveChangesAsync();
        var revision = new BingoTemplateRevision
        {
            HostId = hostId,
            OperationId = Guid.NewGuid(),
            TemplateId = template.Id,
            Revision = 1,
            Dimension = 3,
            CreatedByTwitchUserId = "host-id",
            CreatedByLogin = "channel",
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.BingoTemplateRevisions.Add(revision);
        _ = await db.SaveChangesAsync();
        var game = new BingoGame
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            CreationOperationId = Guid.NewGuid(),
            TemplateRevisionId = revision.Id,
            TemplateName = template.Name,
            TemplateRevisionNumber = 1,
            Dimension = 3,
            Seed = "passport-game",
            Mode = BingoGameMode.Shared,
            Status = BingoGameStatus.Completed,
            CreatedAtUtc = _now.AddHours(-1).UtcDateTime,
            CompletedAtUtc = _now.UtcDateTime,
        };
        _ = db.BingoGames.Add(game);
        _ = await db.SaveChangesAsync();
        var card = new BingoCard
        {
            HostId = hostId,
            GameId = game.Id,
            PublicId = Guid.NewGuid(),
            AssignmentKey = "shared",
            AssignmentName = "Shared card",
            IssuedAtUtc = _now.AddMinutes(-30).UtcDateTime,
        };
        _ = db.BingoCards.Add(card);
        _ = await db.SaveChangesAsync();
        var wins = new[]
        {
            new BingoWin
            {
                HostId = hostId,
                GameId = game.Id,
                CardId = card.Id,
                PublicId = Guid.NewGuid(),
                Kind = BingoWinKind.Row,
                RuleKey = "row:0",
                CompletedAtUtc = _now.AddMinutes(-10).UtcDateTime,
            },
            new BingoWin
            {
                HostId = hostId,
                GameId = game.Id,
                CardId = card.Id,
                PublicId = Guid.NewGuid(),
                Kind = BingoWinKind.Column,
                RuleKey = "column:0",
                CompletedAtUtc = _now.AddMinutes(-5).UtcDateTime,
            },
        };
        db.BingoWins.AddRange(wins);
        _ = await db.SaveChangesAsync();
        db.BingoWinRecipients.AddRange(
            wins.Select(win => new BingoWinRecipient
            {
                HostId = hostId,
                WinId = win.Id,
                TwitchUserId = twitchUserId,
                Login = "viewer",
                DisplayName = "Viewer",
            })
        );
        _ = await db.SaveChangesAsync();
    }

    private static async Task SetFeaturesAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures = features;
        _ = await db.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableStreamLivenessProvider(
        string twitchStreamId,
        DateTimeOffset startedAtUtc
    ) : IHostStreamLivenessProvider
    {
        private HostStreamLivenessOutcome _outcome = new HostStreamLivenessOutcome.Live(
            twitchStreamId,
            startedAtUtc
        );

        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
            IO<HostStreamLivenessOutcome, Never>.Create(_ =>
                ValueTask.FromResult(Result<HostStreamLivenessOutcome, Never>.Success(_outcome))
            );

        public void Set(string streamId, DateTimeOffset streamStartedAtUtc) =>
            _outcome = new HostStreamLivenessOutcome.Live(streamId, streamStartedAtUtc);
    }

    private sealed class GatedStreamLivenessProvider(HostStreamLivenessOutcome outcome)
        : IHostStreamLivenessProvider
    {
        private readonly TaskCompletionSource _bothCallsArrived = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _callCount;

        public Task BothCallsArrived => _bothCallsArrived.Task;

        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
            IO<HostStreamLivenessOutcome, Never>.Create(WaitAsync);

        public void Release() => _release.SetResult();

        private async ValueTask<Result<HostStreamLivenessOutcome, Never>> WaitAsync(
            CancellationToken cancellationToken
        )
        {
            if (Interlocked.Increment(ref _callCount) == 2)
            {
                _bothCallsArrived.SetResult();
            }
            await _release.Task.WaitAsync(cancellationToken);
            return Result<HostStreamLivenessOutcome, Never>.Success(outcome);
        }
    }
}
