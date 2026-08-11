using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
        overlay.AttendanceStreakDays.ShouldBeNull();
        (await projections.GetAutomationPayloadAsync("channel", "subject", default))
            .ShouldNotBeNull()
            .AttendanceStreakDays.ShouldBeNull();
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
    public async Task ChatPresence_IsIdempotentAndDisabledStatePreservesWithoutReplay()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var service = CreateService(database);
        var viewer = new ViewerPassportIdentity("viewer-id", "viewer", "Viewer");

        (await service.RecordChatPresenceAsync("channel", viewer, _now, default)).ShouldBeTrue();
        (
            await service.RecordChatPresenceAsync("channel", viewer, _now.AddHours(2), default)
        ).ShouldBeFalse();
        await SetFeaturesAsync(database, hostId, HostFeatureFlags.None);

        (
            await service.RecordChatPresenceAsync("channel", viewer, _now.AddDays(1), default)
        ).ShouldBeFalse();
        _ = (
            await service.SaveAsync(Save(hostId, "viewer-id", "viewer"), default)
        ).ShouldBeOfType<ViewerPassportMutationOutcome.FeatureDisabled>();
        _ = (
            await service.GetVisibleAsync(
                "channel",
                "viewer",
                ViewerPassportAudience.Anonymous,
                default
            )
        ).ShouldBeOfType<ViewerPassportQueryOutcome.FeatureDisabled>();
        _ = (
            await service.ExportAsync(hostId, viewer, default)
        ).ShouldBeOfType<ViewerPassportExportOutcome.FeatureDisabled>();
        _ = (
            await service.ResetAsync(hostId, "viewer-id", default)
        ).ShouldBeOfType<ViewerPassportResetOutcome.FeatureDisabled>();

        await SetFeaturesAsync(database, hostId, HostFeatureFlags.ViewerPassports);
        var restored = (await service.GetSelfAsync(hostId, viewer, default))
            .ShouldBeOfType<ViewerPassportQueryOutcome.Available>()
            .Passport;
        restored.Statistics.AttendanceStreakDays.ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.ViewerPassportAttendanceDays.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task ChatObserver_IsGatedBeforeCreatingAProfileAndUsesTheRuntimeClock()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.None);
        var service = CreateService(database);
        var observer = new ViewerPassportRuntime(
            service,
            new FixedTimeProvider(_now),
            NullLogger<ViewerPassportRuntime>.Instance
        );
        var message = new ChatMessage(
            "viewer",
            "channel",
            "hello",
            "raw",
            new Dictionary<string, string>
            {
                ["user-id"] = "viewer-id",
                ["display-name"] = "Viewer",
            }
        );

        await observer.MessageReceivedAsync(message, default);
        await using (var disabled = await database.CreateDbContextAsync())
        {
            (await disabled.ViewerPassports.CountAsync()).ShouldBe(0);
            (await disabled.ViewerPassportAttendanceDays.CountAsync()).ShouldBe(0);
        }

        await SetFeaturesAsync(database, hostId, HostFeatureFlags.ViewerPassports);
        await observer.MessageReceivedAsync(message, default);

        await using var enabled = await database.CreateDbContextAsync();
        (await enabled.ViewerPassports.SingleAsync()).DisplayName.ShouldBe("Viewer");
        var attendance = await enabled.ViewerPassportAttendanceDays.SingleAsync();
        attendance.DateUtc.ShouldBe(DateOnly.FromDateTime(_now.UtcDateTime));
        attendance.FirstSeenAtUtc.ShouldBe(_now.UtcDateTime);
    }

    [Test]
    public async Task ExportAndReset_AreHostScopedAndLeaveSourceHistoryIntact()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "channel", HostFeatureFlags.ViewerPassports);
        var viewer = new ViewerPassportIdentity("viewer-id", "viewer", "Viewer");
        var service = CreateService(database);
        _ = Success(await service.SaveAsync(Save(hostId, "viewer-id", "viewer"), default));
        _ = await service.RecordChatPresenceAsync("channel", viewer, _now, default);
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
        export.Sections.Keys.ShouldContain("viewer-passports.attendance-days");
        _ = (
            await service.ResetAsync(hostId, "viewer-id", default)
        ).ShouldBeOfType<ViewerPassportResetOutcome.Succeeded>();

        await using var verify = await database.CreateDbContextAsync();
        (await verify.ViewerPassports.CountAsync()).ShouldBe(0);
        (await verify.ViewerPassportAttendanceDays.CountAsync()).ShouldBe(0);
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("42");
    }

    private static ViewerPassportService CreateService(SqliteBlokeBotDbFactory database) =>
        new(database, new PointBalanceService(database), new FixedTimeProvider(_now));

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
}
