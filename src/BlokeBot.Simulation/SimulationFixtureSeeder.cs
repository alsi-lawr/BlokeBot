using BlokeBot.Announcements;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using PersistedAnnouncementColor = BlokeBot.Persistence.Models.TwitchAnnouncementColor;

namespace BlokeBot.Simulation;

internal sealed class SimulationFixtureSeeder(
    BotHostProvisioningService provisioning,
    IDbContextFactory<BlokeBotDbContext> dbFactory
)
{
    internal const string OverlayAccessKey = "simulation-overlay-access-key-0000000000000";
    internal const string CuePlayerAccessKey = "simulation-cue-player-access-key-000000000000";
    internal const string GiveawayOverlayAccessKey =
        "simulation-giveaway-overlay-key-0000000000000";
    internal const string EventFeedOverlayAccessKey =
        "simulation-event-feed-overlay-key-000000000000";
    internal const string ViewerQueueOverlayAccessKey =
        "simulation-viewer-queue-overlay-key-0000000";

    public async Task<BotHostChoice> SeedAsync(CancellationToken cancellationToken)
    {
        var hostId = await provisioning.EnsureHostAsync(
            FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.Login,
            FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.Id,
            FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.DisplayName,
            null,
            cancellationToken
        );
        var now = SimulationMode.Now.UtcDateTime;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.SingleAsync(x => x.Id == hostId, cancellationToken);
        host.DisplayName = FakeTwitch
            .FakeTwitchScenarioDefinition
            .ReadyDashboard
            .AuthorizedUser
            .DisplayName;
        host.EnabledFeatures = HostFeatureFlags.All;
        host.TimeZoneId = "UTC";

        await SeedGuessingAsync(db, hostId, now, cancellationToken);
        await SeedPointsAsync(db, hostId, now, cancellationToken);
        await SeedBountyAsync(db, hostId, now, cancellationToken);
        await SeedCommunityProgressionAsync(db, hostId, now, cancellationToken);
        await SeedCustomCommandsAsync(db, hostId, now, cancellationToken);
        await SeedRequestBoardAsync(db, hostId, now, cancellationToken);
        await SeedPlayQueueAsync(db, hostId, now, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);
        await SeedMomentAsync(db, hostId, now, cancellationToken);
        await SeedAlertsAsync(db, hostId, now, cancellationToken);
        await SeedAutomaticRaidShoutoutsAsync(db, hostId, now, cancellationToken);
        await SeedOverlayAsync(db, hostId, now, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);

        return new BotHostChoice(
            hostId,
            FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.Login,
            FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.DisplayName,
            AuthRole.Streamer
        );
    }

    private static async Task SeedBountyAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var publicId = Guid.Parse("3e25c2dc-6bc2-41fc-8808-055677f26195");
        if (await db.Bounties.AnyAsync(value => value.PublicId == publicId, cancellationToken))
        {
            return;
        }

        _ = db.Bounties.Add(
            new Bounty
            {
                HostId = hostId,
                PublicId = publicId,
                CreationOperationId = Guid.Parse("2fc49a64-88e8-4e64-a311-b93c91c1482f"),
                CreationFingerprint = "simulation-bounty",
                Title = "Community speedrun challenge",
                Description = "Fund a no-reset attempt before the end of tonight's stream.",
                Status = BountyStatus.Funding,
                Visibility = BountyVisibility.Public,
                FailurePledgePolicy = BountyFailurePledgePolicy.Refund,
                RewardDistribution = BountyRewardDistribution.Proportional,
                FundingTarget = "1500",
                PledgedAmount = "900",
                ContributorCount = 2,
                CompletionReward = "300",
                ExpiresAtUtc = now.AddHours(4),
                Revision = 2,
                CreatedAtUtc = now.AddMinutes(-35),
                UpdatedAtUtc = now.AddMinutes(-12),
                Pledges =
                [
                    new BountyPledge
                    {
                        HostId = hostId,
                        OperationId = Guid.Parse("73605d09-c245-429a-a4ba-ec4319dc14e7"),
                        CommandFingerprint = "simulation-nightowl-pledge",
                        ContributorTwitchUserId = "simulation-nightowl-id",
                        ContributorLogin = "nightowl",
                        Amount = "600",
                        State = BountyPledgeState.Reserved,
                        CreatedAtUtc = now.AddMinutes(-20),
                        UpdatedAtUtc = now.AddMinutes(-20),
                    },
                    new BountyPledge
                    {
                        HostId = hostId,
                        OperationId = Guid.Parse("41259cd6-8247-4494-9aba-10e99990d50d"),
                        CommandFingerprint = "simulation-chatregular-pledge",
                        ContributorTwitchUserId = "simulation-chatregular-id",
                        ContributorLogin = "chatregular",
                        Amount = "300",
                        State = BountyPledgeState.Reserved,
                        CreatedAtUtc = now.AddMinutes(-12),
                        UpdatedAtUtc = now.AddMinutes(-12),
                    },
                ],
                Audits =
                [
                    new BountyModerationAudit
                    {
                        HostId = hostId,
                        OperationId = Guid.Parse("9b088d25-f405-4e5e-88d2-98a419618c5f"),
                        CommandFingerprint = "simulation-created",
                        Action = BountyAuditAction.Created,
                        FromStatus = BountyStatus.Proposed,
                        ToStatus = BountyStatus.Proposed,
                        ActorTwitchUserId = SimulationMode.UserId,
                        ActorLogin = SimulationMode.Login,
                        Reason = "Prepared for the community challenge segment.",
                        BountyRevision = 1,
                        OccurredAtUtc = now.AddMinutes(-35),
                    },
                    new BountyModerationAudit
                    {
                        HostId = hostId,
                        OperationId = Guid.Parse("d0d33038-0f9d-401e-b56d-07ef8b02246d"),
                        CommandFingerprint = "simulation-opened",
                        Action = BountyAuditAction.FundingOpened,
                        FromStatus = BountyStatus.Proposed,
                        ToStatus = BountyStatus.Funding,
                        ActorTwitchUserId = SimulationMode.UserId,
                        ActorLogin = SimulationMode.Login,
                        Reason = "Funding opened after the warm-up run.",
                        BountyRevision = 2,
                        OccurredAtUtc = now.AddMinutes(-30),
                    },
                ],
            }
        );
    }

    private static async Task SeedCommunityProgressionAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var publicId = Guid.Parse("107163c8-39d5-4ba5-896f-142f72245568");
        if (
            await db.CommunitySeasons.AnyAsync(
                value => value.PublicId == publicId,
                cancellationToken
            )
        )
        {
            return;
        }

        var season = new CommunitySeason
        {
            PublicId = publicId,
            HostId = hostId,
            CreationOperationId = Guid.Parse("446c12d3-8aef-4317-9140-ac043286aa8c"),
            Name = "Summer community climb",
            Description = "Complete stream quests together and unlock permanent channel flair.",
            ModeratorNotes = "Simulation-only private note",
            Status = CommunitySeasonStatus.Open,
            Visibility = CommunityVisibility.Public,
            StartsAtUtc = now.AddDays(-5),
            EndsAtUtc = now.AddDays(25),
            OpenedAtUtc = now.AddDays(-5),
            Revision = 2,
            CreatedAtUtc = now.AddDays(-6),
            UpdatedAtUtc = now.AddMinutes(-4),
        };
        var title = new CommunityRewardDefinition
        {
            PublicId = Guid.Parse("6cfdc3df-2f55-440c-b273-54e1b80ad5dc"),
            HostId = hostId,
            Key = "trailblazer",
            Kind = CommunityRewardKind.Title,
            Name = "Trailblazer",
            PresentationToken = "trailblazer",
            CreatedAtUtc = now.AddDays(-6),
            Season = season,
        };
        var badge = new CommunityRewardDefinition
        {
            PublicId = Guid.Parse("add90a84-85fe-4798-aa74-e92810d8dc94"),
            HostId = hostId,
            Key = "summer-star",
            Kind = CommunityRewardKind.Badge,
            Name = "Summer star",
            PresentationToken = "star",
            CreatedAtUtc = now.AddDays(-6),
            Season = season,
        };
        var achievement = new CommunityDefinition
        {
            PublicId = Guid.Parse("6db0bd9a-9e69-49c5-a4bd-b6fa2604d30c"),
            HostId = hostId,
            Key = "first-cheer",
            Name = "Bring the energy",
            Description = "Cheer during the summer climb.",
            Kind = CommunityDefinitionKind.Achievement,
            Scope = CommunityProgressScope.Viewer,
            CompletionMode = CommunityCompletionMode.OneTime,
            EventRule = CommunityEventRuleKind.Cheer,
            Increment = CommunityProgressIncrement.Occurrence,
            Target = 1,
            PointsReward = "100",
            ResetCadence = CommunityResetCadence.None,
            ResetLocalTime = "00:00",
            ScheduleRevision = 1,
            CreatedAtUtc = now.AddDays(-6),
            Season = season,
        };
        var daily = new CommunityDefinition
        {
            PublicId = Guid.Parse("470d3ef9-8c31-48f5-9c03-79f5c497360d"),
            HostId = hostId,
            Key = "daily-chat",
            Name = "Daily regular",
            Description = "Join five chat moments each day.",
            Kind = CommunityDefinitionKind.Quest,
            Scope = CommunityProgressScope.Viewer,
            CompletionMode = CommunityCompletionMode.Repeatable,
            EventRule = CommunityEventRuleKind.ChatMessage,
            Increment = CommunityProgressIncrement.Occurrence,
            Target = 5,
            PointsReward = "25",
            ResetCadence = CommunityResetCadence.Daily,
            ResetLocalTime = "06:00",
            ScheduleRevision = 1,
            CreatedAtUtc = now.AddDays(-6),
            Season = season,
        };
        _ = db.CommunitySeasons.Add(season);
        db.CommunityRewardDefinitions.AddRange(title, badge);
        db.CommunityDefinitions.AddRange(achievement, daily);
        _ = await db.SaveChangesAsync(cancellationToken);
        db.CommunityDefinitionRewards.AddRange(
            new CommunityDefinitionReward
            {
                DefinitionId = achievement.Id,
                RewardDefinitionId = title.Id,
            },
            new CommunityDefinitionReward
            {
                DefinitionId = achievement.Id,
                RewardDefinitionId = badge.Id,
            }
        );
        var completion = new CommunityCompletion
        {
            PublicId = Guid.Parse("69655fd8-eb0e-4e73-8dd2-47d4b506bf92"),
            HostId = hostId,
            SeasonId = season.Id,
            DefinitionId = achievement.Id,
            SubjectKey = "viewer:3000",
            ViewerTwitchUserId = "3000",
            ViewerLogin = "nightowl",
            ViewerDisplayName = "NightOwl",
            DefinitionKey = achievement.Key,
            DefinitionName = achievement.Name,
            Sequence = 1,
            PointsGranted = "100",
            RewardSnapshot =
                "[{\"key\":\"trailblazer\",\"kind\":\"Title\",\"name\":\"Trailblazer\"}]",
            SourceOperationKey = "simulation-cheer",
            CompletedAtUtc = now.AddHours(-2),
        };
        _ = db.CommunityCompletions.Add(completion);
        db.CommunityProgress.AddRange(
            new CommunityProgress
            {
                HostId = hostId,
                SeasonId = season.Id,
                DefinitionId = achievement.Id,
                SubjectKey = "viewer:3000",
                ViewerTwitchUserId = "3000",
                ViewerLogin = "nightowl",
                ViewerDisplayName = "NightOwl",
                Amount = 1,
                CompletionCount = 1,
                UpdatedAtUtc = now.AddHours(-2),
            },
            new CommunityProgress
            {
                HostId = hostId,
                SeasonId = season.Id,
                DefinitionId = daily.Id,
                SubjectKey = "viewer:simulation-chatregular-id",
                ViewerTwitchUserId = "simulation-chatregular-id",
                ViewerLogin = "chatregular",
                ViewerDisplayName = "ChatRegular",
                Amount = 3,
                CompletionCount = 2,
                PeriodKey = "v1:Daily:simulation",
                UpdatedAtUtc = now.AddMinutes(-4),
            }
        );
        _ = await db.SaveChangesAsync(cancellationToken);
        db.CommunityRewardUnlocks.AddRange(
            new CommunityRewardUnlock
            {
                HostId = hostId,
                RewardDefinitionId = title.Id,
                ViewerTwitchUserId = "3000",
                ViewerLogin = "nightowl",
                ViewerDisplayName = "NightOwl",
                CompletionId = completion.Id,
                GrantedAtUtc = completion.CompletedAtUtc,
            },
            new CommunityRewardUnlock
            {
                HostId = hostId,
                RewardDefinitionId = badge.Id,
                ViewerTwitchUserId = "3000",
                ViewerLogin = "nightowl",
                ViewerDisplayName = "NightOwl",
                CompletionId = completion.Id,
                GrantedAtUtc = completion.CompletedAtUtc,
            }
        );
        _ = db.CommunityEquippedRewards.Add(
            new CommunityEquippedReward
            {
                HostId = hostId,
                Kind = CommunityRewardKind.Title,
                RewardDefinitionId = title.Id,
                ViewerTwitchUserId = "3000",
                ViewerLogin = "nightowl",
                LastOperationId = Guid.Parse("f02c9d94-b393-4bf6-816b-84a031da8f9c"),
                EquippedAtUtc = now.AddHours(-1),
            }
        );
    }

    private static async Task SeedOverlayAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (
            !await db.OverlayInstances.AnyAsync(
                value => value.PublicId == Guid.Parse("6fd2b259-4308-4ab3-995c-4029c22f7354"),
                cancellationToken
            )
        )
        {
            var feed = new OverlayInstance
            {
                PublicId = Guid.Parse("6fd2b259-4308-4ab3-995c-4029c22f7354"),
                HostId = hostId,
                Name = "Channel event feed",
                Type = OverlayType.EventFeed,
                IsEnabled = true,
                ConfigurationJson =
                    """{"schemaVersion":1,"capacity":10,"overflowPolicy":"dropNewest","kinds":{"pointAward":{"enabled":true,"template":"{recipient} received {amount} {pointLabel}","priority":"normal","durationSeconds":6},"guessingWinner":{"enabled":false,"template":"{winners} won {roundName}: {winningAnswer}","priority":"high","durationSeconds":8},"giveawayWinner":{"enabled":true,"template":"{winners} won {prizes}","priority":"high","durationSeconds":8}},"appearance":{"x":110,"y":90,"width":1120,"height":720,"css":".accent{fill:#f472b6;}"}}""",
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(EventFeedOverlayAccessKey),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            _ = db.OverlayInstances.Add(feed);
            db.OverlayEventFeedItems.AddRange(
                new OverlayEventFeedItem
                {
                    OverlayInstance = feed,
                    HostId = hostId,
                    Kind = OverlayEventFeedKind.GiveawayWinner,
                    SourceKey = "simulation-giveaway",
                    Priority = OverlayEventFeedPriority.High,
                    Lifecycle = OverlayEventFeedLifecycle.Active,
                    Title = "Giveaway winner",
                    Body = "nightowl, newviewer won 500 points, 250 points",
                    DurationSeconds = 8,
                    EnqueuedAtUtc = now,
                    DisplayDeadlineUtc = now.AddHours(1),
                },
                new OverlayEventFeedItem
                {
                    OverlayInstance = feed,
                    HostId = hostId,
                    Kind = OverlayEventFeedKind.PointAward,
                    SourceKey = "simulation-points",
                    Priority = OverlayEventFeedPriority.Normal,
                    Lifecycle = OverlayEventFeedLifecycle.Queued,
                    Title = "Points awarded",
                    Body = "helpfulviewer received 100 points",
                    DurationSeconds = 6,
                    EnqueuedAtUtc = now.AddSeconds(1),
                }
            );
        }

        if (
            !await db.OverlayInstances.AnyAsync(
                value => value.PublicId == Guid.Parse("1a10c145-e2d2-4605-9659-7250b191b5a1"),
                cancellationToken
            )
        )
        {
            var queueId = await db
                .PlayQueues.Where(value => value.HostId == hostId && value.Slug == "main")
                .Select(value => value.Id)
                .SingleAsync(cancellationToken);
            _ = db.OverlayInstances.Add(
                new OverlayInstance
                {
                    PublicId = Guid.Parse("1a10c145-e2d2-4605-9659-7250b191b5a1"),
                    HostId = hostId,
                    Name = "Viewer queue",
                    Type = OverlayType.ViewerQueue,
                    IsEnabled = true,
                    ConfigurationJson =
                        $$$"""{"schemaVersion":1,"queueId":{{{queueId}}},"currentRows":4,"nextRows":6,"appearance":{"x":160,"y":140,"width":1200,"height":800,"css":""}}""",
                    AccessKeyDigest = OverlayAccessKeyDigest.Compute(ViewerQueueOverlayAccessKey),
                    KeyVersion = 1,
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
        }

        if (
            !await db.OverlayInstances.AnyAsync(
                value => value.PublicId == Guid.Parse("0a8b9ee0-500f-4b20-b706-455ff9ef4288"),
                cancellationToken
            )
        )
        {
            _ = db.OverlayInstances.Add(
                new OverlayInstance
                {
                    PublicId = Guid.Parse("0a8b9ee0-500f-4b20-b706-455ff9ef4288"),
                    HostId = hostId,
                    Name = "Points giveaway",
                    Type = OverlayType.Giveaway,
                    IsEnabled = true,
                    ConfigurationJson =
                        """{"schemaVersion":1,"title":"Community points giveaway","showEntrantCount":true,"showCountdown":true,"showJoinCommand":true,"appearance":{"x":160,"y":690,"width":1600,"height":270,"css":""}}""",
                    AccessKeyDigest = OverlayAccessKeyDigest.Compute(GiveawayOverlayAccessKey),
                    KeyVersion = 1,
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
        }

        if (
            await db.OverlayInstances.AnyAsync(
                value => value.PublicId == Guid.Parse("82bd3021-fc60-47fc-8fa7-ed828083e70a"),
                cancellationToken
            )
        )
        {
            return;
        }

        _ = db.OverlayInstances.Add(
            new OverlayInstance
            {
                PublicId = Guid.Parse("82bd3021-fc60-47fc-8fa7-ed828083e70a"),
                HostId = hostId,
                Name = "Guessing round",
                Type = OverlayType.Guessing,
                IsEnabled = true,
                ConfigurationJson =
                    """{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":8,"appearance":{"x":160,"y":690,"width":1600,"height":270,"css":""}}""",
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(OverlayAccessKey),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        _ = db.OverlayInstances.Add(
            new OverlayInstance
            {
                PublicId = Guid.Parse("a24ea34e-47f7-41f7-bdf7-5de18d90389c"),
                HostId = hostId,
                Name = "Celebration cue player",
                Type = OverlayType.CuePlayer,
                IsEnabled = true,
                ConfigurationJson = """{"schemaVersion":1}""",
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(CuePlayerAccessKey),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        _ = db.OverlayCues.Add(
            new OverlayCue
            {
                PublicId = Guid.Parse("f9c437a7-4df5-45de-bb87-450ca6a40f9b"),
                HostId = hostId,
                Name = "Raid celebration",
                IsEnabled = true,
                DurationMilliseconds = 8000,
                QueuePolicy = OverlayCueQueuePolicy.Enqueue,
                ConfigurationJson =
                    """{"schemaVersion":1,"layers":[{"type":"externalWeb","url":"https://example.com/","startOffsetMilliseconds":0,"durationMilliseconds":8000,"zIndex":0,"rectangle":{"xPercent":10,"yPercent":10,"widthPercent":80,"heightPercent":80}}]}""",
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
    }

    private static async Task SeedGuessingAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var profile = await db.Profiles.SingleAsync(
            x => x.HostId == hostId && x.IsDefault,
            cancellationToken
        );
        if (
            !await db.GuessOptions.AnyAsync(
                x => x.GuessRoundProfileId == profile.Id,
                cancellationToken
            )
        )
        {
            profile.WinningGuessPointReward = "250";
            profile.Options.AddRange([
                new GuessOption
                {
                    Name = "Blue",
                    ReplyText = "@{user} picked Blue.",
                    SortOrder = 0,
                },
                new GuessOption
                {
                    Name = "Red",
                    ReplyText = "@{user} picked Red.",
                    SortOrder = 1,
                },
                new GuessOption
                {
                    Name = "Gold",
                    ReplyText = "@{user} picked Gold.",
                    SortOrder = 2,
                },
            ]);
        }

        if (await db.Rounds.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            return;
        }

        db.Rounds.AddRange(
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Completed,
                StartedAtUtc = now.AddHours(-3),
                ClosedAtUtc = now.AddHours(-2).AddMinutes(-45),
                WinningName = "Blue",
                Votes =
                [
                    Vote("nightowl", "Blue", now.AddHours(-2).AddMinutes(-58)),
                    Vote("chatregular", "Red", now.AddHours(-2).AddMinutes(-56)),
                    Vote("newviewer", "Blue", now.AddHours(-2).AddMinutes(-54)),
                ],
            },
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Completed,
                StartedAtUtc = now.AddHours(-2),
                ClosedAtUtc = now.AddHours(-1).AddMinutes(-45),
                WinningName = "Red",
                Votes =
                [
                    Vote("nightowl", "Gold", now.AddHours(-1).AddMinutes(-58)),
                    Vote("chatregular", "Red", now.AddHours(-1).AddMinutes(-56)),
                    Vote("newviewer", "Red", now.AddHours(-1).AddMinutes(-52)),
                ],
            },
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Open,
                StartedAtUtc = now.AddMinutes(-8),
                Votes =
                [
                    Vote("nightowl", "Blue", now.AddMinutes(-7)),
                    Vote("chatregular", "Gold", now.AddMinutes(-6)),
                    Vote("newviewer", "Blue", now.AddMinutes(-5)),
                    Vote("firsttimer", "Red", now.AddMinutes(-3)),
                ],
            }
        );
    }

    private static GuessVote Vote(string login, string guess, DateTime guessedAtUtc) =>
        new GuessVote
        {
            Login = login,
            GuessName = guess,
            GuessedAtUtc = guessedAtUtc,
        };

    private static async Task SeedPointsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (!await db.PointBalances.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            var balances = new[]
            {
                Balance(hostId, "nightowl", "1840", now.AddMinutes(-12)),
                Balance(hostId, "chatregular", "1325", now.AddMinutes(-18)),
                Balance(hostId, "newviewer", "910", now.AddMinutes(-25)),
                Balance(hostId, "firsttimer", "250", now.AddMinutes(-32)),
            };
            db.PointBalances.AddRange(balances);
            db.PointLedgerEntries.AddRange(
                balances.Select(
                    (balance, index) =>
                        new PointLedgerEntry
                        {
                            HostId = hostId,
                            Kind = PointLedgerKind.Add,
                            Login = balance.Login,
                            Delta = balance.Amount,
                            BalanceAfter = balance.Amount,
                            ActorLogin = FakeTwitch
                                .FakeTwitchScenarioDefinition
                                .ReadyDashboard
                                .AuthorizedUser
                                .Login,
                            Note = "Stream reward",
                            CreatedAtUtc = now.AddMinutes(-32 + (index * 6)),
                        }
                )
            );
        }

        if (await db.PointsGiveaways.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            return;
        }

        _ = db.PointsGiveaways.Add(
            new PointsGiveaway
            {
                HostId = hostId,
                Status = PointsGiveawayStatus.Active,
                StartedAtUtc = now.AddMinutes(-10),
                EndsAtUtc = now.AddMinutes(20),
                MinimumPayout = "100",
                MaximumPayout = "500",
                WinnerCount = 2,
                Eligibility = PointsEligibilityMode.Everyone,
                Entrants =
                [
                    new PointsGiveawayEntrant
                    {
                        Login = "nightowl",
                        JoinedAtUtc = now.AddMinutes(-9),
                    },
                    new PointsGiveawayEntrant
                    {
                        Login = "chatregular",
                        JoinedAtUtc = now.AddMinutes(-8),
                    },
                    new PointsGiveawayEntrant
                    {
                        Login = "newviewer",
                        JoinedAtUtc = now.AddMinutes(-7),
                    },
                ],
            }
        );
    }

    private static PointBalance Balance(
        int hostId,
        string login,
        string amount,
        DateTime updatedAtUtc
    ) =>
        new PointBalance
        {
            HostId = hostId,
            Login = login,
            Amount = amount,
            UpdatedAtUtc = updatedAtUtc,
        };

    private static async Task SeedCustomCommandsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (await db.CustomCommands.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            return;
        }

        var welcome = new CustomMessageLibraryEntry
        {
            HostId = hostId,
            Name = "Welcome",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Variants =
            [
                new CustomMessageVariant
                {
                    SortOrder = 0,
                    Text = "Welcome in, @{user}! Make yourself at home.",
                },
                new CustomMessageVariant { SortOrder = 1, Text = "Good to see you, @{user}." },
            ],
        };
        var hydrationReply = new CustomMessageLibraryEntry
        {
            HostId = hostId,
            Name = "Hydration reminder",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Variants =
            [
                new CustomMessageVariant
                {
                    SortOrder = 0,
                    Text = "Hydration reminder number {count}: take a sip of water.",
                },
            ],
        };
        var counter = new CustomCounter
        {
            HostId = hostId,
            Name = "Hydration reminders",
            Value = 12,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.AddRange(welcome, hydrationReply, counter);
        _ = await db.SaveChangesAsync(cancellationToken);

        db.CustomCommands.AddRange(
            new CustomCommand
            {
                HostId = hostId,
                Name = "Welcome viewer",
                CooldownSeconds = 10,
                Action = new MessageCustomCommandAction
                {
                    HostId = hostId,
                    ZeroArgumentMessageLibraryEntryId = welcome.Id,
                },
                Aliases =
                [
                    new CustomCommandAlias
                    {
                        HostId = hostId,
                        Alias = "welcome",
                        SortOrder = 0,
                    },
                    new CustomCommandAlias
                    {
                        HostId = hostId,
                        Alias = "hello",
                        SortOrder = 1,
                    },
                ],
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
            new CustomCommand
            {
                HostId = hostId,
                Name = "Hydration counter",
                CooldownSeconds = 30,
                Action = new CounterCustomCommandAction
                {
                    HostId = hostId,
                    ZeroArgumentMessageLibraryEntryId = hydrationReply.Id,
                    CounterId = counter.Id,
                },
                Aliases =
                [
                    new CustomCommandAlias
                    {
                        HostId = hostId,
                        Alias = "hydrate",
                        SortOrder = 0,
                    },
                ],
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
            new CustomCommand
            {
                HostId = hostId,
                Name = "Raid celebration cue",
                CooldownSeconds = 15,
                Action = new OverlayCueCustomCommandAction
                {
                    HostId = hostId,
                    ZeroArgumentMessageLibraryEntryId = welcome.Id,
                    TargetOverlayPublicId = Guid.Parse("a24ea34e-47f7-41f7-bdf7-5de18d90389c"),
                    CuePublicId = Guid.Parse("f9c437a7-4df5-45de-bb87-450ca6a40f9b"),
                    QueuePolicy = OverlayCueQueuePolicy.Enqueue,
                    ReplyOrder = OverlayCueReplyOrder.After,
                },
                Aliases =
                [
                    new CustomCommandAlias
                    {
                        HostId = hostId,
                        Alias = "celebrate",
                        SortOrder = 0,
                    },
                ],
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        _ = db.CustomCommands.Add(
            new CustomCommand
            {
                HostId = hostId,
                Name = "Access policy fixture",
                Enabled = true,
                AllowEveryone = false,
                AllowModerators = true,
                AllowedUsers =
                [
                    new CustomCommandAllowedUser
                    {
                        HostId = hostId,
                        TwitchUserId = "3000",
                        Login = "trustedviewer",
                        DisplayName = "Trusted Viewer",
                    },
                ],
                Action = new MessageCustomCommandAction
                {
                    HostId = hostId,
                    ZeroArgumentMessageLibraryEntryId = welcome.Id,
                },
                Aliases =
                [
                    new CustomCommandAlias
                    {
                        HostId = hostId,
                        Alias = "modfixture",
                        SortOrder = 0,
                    },
                ],
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        _ = db.CustomCommands.Add(
            new CustomCommand
            {
                HostId = hostId,
                Name = "Legacy fixed-route collision",
                Enabled = true,
                Action = new MessageCustomCommandAction
                {
                    HostId = hostId,
                    ZeroArgumentMessageLibraryEntryId = welcome.Id,
                },
                Aliases =
                [
                    new CustomCommandAlias
                    {
                        HostId = hostId,
                        Alias = "moment",
                        SortOrder = 0,
                    },
                ],
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        for (var index = 1; index <= 32; index++)
        {
            _ = db.CustomCommands.Add(
                new CustomCommand
                {
                    HostId = hostId,
                    Name = $"Catalog fixture {index:00}",
                    Enabled = true,
                    Action = new MessageCustomCommandAction
                    {
                        HostId = hostId,
                        ZeroArgumentMessageLibraryEntryId = welcome.Id,
                    },
                    Aliases =
                    [
                        new CustomCommandAlias
                        {
                            HostId = hostId,
                            Alias = $"viewercommand{index:00}",
                            SortOrder = 0,
                        },
                    ],
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
        }

        _ = db.CustomAnnouncements.Add(
            new CustomAnnouncement
            {
                HostId = hostId,
                Name = "Welcome reminder",
                Enabled = false,
                MessageLibraryEntryId = welcome.Id,
                DeliveryPolicy = new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
                {
                    HostId = hostId,
                    RetryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(2)),
                    OccurrenceLifetime = new AnnouncementOccurrenceLifetime(
                        TimeSpan.FromSeconds(30)
                    ),
                },
                Schedule = new IntervalAfterChatCustomAnnouncementSchedule
                {
                    HostId = hostId,
                    IntervalMinutes = 30,
                    RequiredChatMessages = 5,
                },
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
    }

    private static async Task SeedRequestBoardAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (await db.RequestBoards.AnyAsync(value => value.HostId == hostId, cancellationToken))
        {
            return;
        }

        _ = db.RequestBoards.Add(
            new RequestBoard
            {
                HostId = hostId,
                Slug = "requests",
                Title = "Game night requests",
                Description = "Share games you would like to see on the next community night.",
                IsOpen = true,
                VotingEnabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
    }

    private static async Task SeedPlayQueueAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (await db.PlayQueues.AnyAsync(value => value.HostId == hostId, cancellationToken))
        {
            return;
        }

        var platform = new PlayQueueField
        {
            Position = 0,
            Key = "platform",
            Label = "Platform",
            Choices = "PC\nConsole",
        };
        var role = new PlayQueueField
        {
            Position = 1,
            Key = "preferred-role",
            Label = "Preferred role",
            Choices = "Tank\nSupport\nDamage",
        };
        var queue = new PlayQueue
        {
            HostId = hostId,
            Slug = "main",
            Name = "Community night",
            ActivityName = "BlokeQuest",
            Capacity = 2,
            IsOpen = true,
            SelectionMode = PlayQueueSelectionMode.JoinOrder,
            ShowParticipantNames = true,
            CurrentPartyNumber = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Fields = [platform, role],
        };
        queue.Entries.AddRange(
            Entry("nightowl", "NightOwl", PlayQueueEntryStatus.Selected, -8, 1, "PC", "Tank"),
            Entry(
                "newviewer",
                "NewViewer",
                PlayQueueEntryStatus.Selected,
                -7,
                1,
                "Console",
                "Support"
            ),
            Entry(
                "playerthree",
                "PlayerThree",
                PlayQueueEntryStatus.Waiting,
                -6,
                null,
                "PC",
                "Damage"
            ),
            Entry("playerfour", "PlayerFour", PlayQueueEntryStatus.Waiting, -5, null, "PC", ""),
            Entry(
                "playerfive",
                "PlayerFive",
                PlayQueueEntryStatus.Waiting,
                -4,
                null,
                "Console",
                "Support"
            )
        );
        _ = db.PlayQueues.Add(queue);
        return;

        PlayQueueEntry Entry(
            string login,
            string displayName,
            PlayQueueEntryStatus status,
            int joinedMinutes,
            int? partyNumber,
            string platformValue,
            string roleValue
        ) =>
            new()
            {
                HostId = hostId,
                IdentityKey = $"id:simulation-{login}",
                TwitchUserId = $"simulation-{login}",
                NormalizedLogin = login,
                DisplayName = displayName,
                Status = status,
                PartyNumber = partyNumber,
                JoinedAtUtc = now.AddMinutes(joinedMinutes),
                UpdatedAtUtc = now,
                Values =
                [
                    new PlayQueueEntryValue { Field = platform, Value = platformValue },
                    new PlayQueueEntryValue { Field = role, Value = roleValue },
                ],
            };
    }

    private static async Task SeedMomentAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (await db.MomentCandidates.AnyAsync(value => value.HostId == hostId, cancellationToken))
        {
            return;
        }

        var capturedAt = now.AddMinutes(-12);
        _ = db.MomentCandidates.Add(
            new MomentCandidate
            {
                PublicId = Guid.Parse("75a75ee9-cfed-47da-ad88-762f67f8c0a5"),
                HostId = hostId,
                StreamIdentity = "stream-0001",
                State = MomentCandidateState.Approved,
                PublicTitle = "Community clutch save",
                PublicCategory = "Community",
                CapturedAtUtc = capturedAt,
                LastCapturedAtUtc = capturedAt,
                ApprovedAtUtc = now.AddMinutes(-10),
                Contributors =
                [
                    new MomentContributor
                    {
                        IdentityKey = "id:viewer-1000",
                        TwitchUserId = "viewer-1000",
                        NormalizedLogin = "nightowl",
                        DisplayName = "NightOwl",
                        CaptureCount = 1,
                        FirstCapturedAtUtc = capturedAt,
                        LastCapturedAtUtc = capturedAt,
                    },
                ],
                Votes =
                [
                    new MomentVote
                    {
                        IdentityKey = "id:viewer-1001",
                        TwitchUserId = "viewer-1001",
                        NormalizedLogin = "clipfan",
                        CreatedAtUtc = now.AddMinutes(-8),
                    },
                ],
            }
        );
    }

    private static async Task SeedAlertsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (await db.DurableAlerts.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            return;
        }

        _ = db.DurableAlerts.Add(
            new DurableAlert
            {
                HostId = hostId,
                Severity = DurableAlertSeverity.Info,
                Source = "simulation",
                SourceKey = "simulation-reconnected",
                Title = "Chat connection restored",
                Message = "Queued messages resumed after the connection recovered.",
                CreatedAtUtc = now.AddHours(-2),
                AcknowledgedAtUtc = now.AddHours(-1).AddMinutes(-45),
                AcknowledgedByLogin = FakeTwitch
                    .FakeTwitchScenarioDefinition
                    .ReadyDashboard
                    .AuthorizedUser
                    .Login,
            }
        );
    }

    private static async Task SeedAutomaticRaidShoutoutsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (
            !await db.AutomaticRaidShoutoutSettings.AnyAsync(
                value => value.HostId == hostId,
                cancellationToken
            )
        )
        {
            _ = db.AutomaticRaidShoutoutSettings.Add(
                new AutomaticRaidShoutoutSettings
                {
                    HostId = hostId,
                    Enabled = true,
                    MinimumViewerCount = 10,
                    Mechanism = AutomaticRaidShoutoutMechanism.Chat,
                    ChatPresentation = AutomaticRaidChatPresentation.Pinned,
                    MessageTemplate =
                        "Welcome {twitch_handle}! Last seen playing {last_game|something fun}: {channel_url}",
                    PinDurationSeconds = 300,
                    AnnouncementColor = PersistedAnnouncementColor.Purple,
                    UpdatedAtUtc = now,
                }
            );
        }

        if (
            await db.AutomaticRaidShoutoutOutcomes.AnyAsync(
                value => value.HostId == hostId,
                cancellationToken
            )
        )
        {
            return;
        }

        db.AutomaticRaidShoutoutOutcomes.AddRange(
            Outcome(
                hostId,
                "simulation-raid-partial",
                "pinpal",
                "Pin Pal",
                84,
                AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
                AutomaticRaidShoutoutResultCode.PartialFailure,
                now.AddMinutes(-8)
            ),
            Outcome(
                hostId,
                "simulation-raid-delivered",
                "cozystreamer",
                "Cozy Streamer",
                42,
                AutomaticRaidShoutoutOutcomeStatus.Delivered,
                AutomaticRaidShoutoutResultCode.Delivered,
                now.AddMinutes(-24)
            ),
            Outcome(
                hostId,
                "simulation-raid-authority",
                "newfriend",
                "New Friend",
                21,
                AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
                AutomaticRaidShoutoutResultCode.AuthorityRequired,
                now.AddMinutes(-41)
            ),
            Outcome(
                hostId,
                "simulation-raid-cooldown",
                "speedrunner",
                "Speed Runner",
                16,
                AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
                AutomaticRaidShoutoutResultCode.Cooldown,
                now.AddMinutes(-58)
            )
        );
    }

    private static AutomaticRaidShoutoutOutcome Outcome(
        int hostId,
        string providerMessageId,
        string sourceLogin,
        string sourceDisplayName,
        int viewerCount,
        AutomaticRaidShoutoutOutcomeStatus status,
        AutomaticRaidShoutoutResultCode resultCode,
        DateTime timestamp
    ) =>
        new AutomaticRaidShoutoutOutcome
        {
            HostId = hostId,
            ProviderMessageId = providerMessageId,
            SourceTwitchUserId = $"{sourceLogin}-id",
            SourceLogin = sourceLogin,
            SourceDisplayName = sourceDisplayName,
            ViewerCount = viewerCount,
            Status = status,
            ResultCode = resultCode,
            MessageTimestampUtc = timestamp,
            ClaimedAtUtc = timestamp,
            CompletedAtUtc = timestamp.AddSeconds(2),
        };
}
