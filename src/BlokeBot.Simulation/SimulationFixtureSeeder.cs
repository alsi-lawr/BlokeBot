using BlokeBot.Announcements;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
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
        await SeedCustomCommandsAsync(db, hostId, now, cancellationToken);
        await SeedRequestBoardAsync(db, hostId, now, cancellationToken);
        await SeedPlayQueueAsync(db, hostId, now, cancellationToken);
        await SeedMomentAsync(db, hostId, now, cancellationToken);
        await SeedAlertsAsync(db, hostId, now, cancellationToken);
        await SeedAutomaticRaidShoutoutsAsync(db, hostId, now, cancellationToken);
        await SeedOverlayAsync(db, hostId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new BotHostChoice(
            hostId,
            FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.Login,
            FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.DisplayName,
            AuthRole.Streamer
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
                    """{"schemaVersion":1,"capacity":10,"overflowPolicy":"dropNewest","kinds":{"pointAward":{"enabled":true,"template":"{recipient} received {amount} {pointLabel}","priority":"normal","durationSeconds":6},"guessingWinner":{"enabled":true,"template":"{winners} won {roundName}: {winningAnswer}","priority":"high","durationSeconds":8},"giveawayWinner":{"enabled":true,"template":"{winners} won {prizes}","priority":"high","durationSeconds":8}}}""",
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(EventFeedOverlayAccessKey),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.OverlayInstances.Add(feed);
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
                value => value.PublicId == Guid.Parse("0a8b9ee0-500f-4b20-b706-455ff9ef4288"),
                cancellationToken
            )
        )
        {
            db.OverlayInstances.Add(
                new OverlayInstance
                {
                    PublicId = Guid.Parse("0a8b9ee0-500f-4b20-b706-455ff9ef4288"),
                    HostId = hostId,
                    Name = "Points giveaway",
                    Type = OverlayType.Giveaway,
                    IsEnabled = true,
                    ConfigurationJson =
                        """{"schemaVersion":1,"title":"Community points giveaway","showEntrantCount":true,"showCountdown":true,"showJoinCommand":true}""",
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

        db.OverlayInstances.Add(
            new OverlayInstance
            {
                PublicId = Guid.Parse("82bd3021-fc60-47fc-8fa7-ed828083e70a"),
                HostId = hostId,
                Name = "Guessing round",
                Type = OverlayType.Guessing,
                IsEnabled = true,
                ConfigurationJson =
                    """{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":8}""",
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(OverlayAccessKey),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        db.OverlayInstances.Add(
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
        db.OverlayCues.Add(
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
                            CreatedAtUtc = now.AddMinutes(-32 + index * 6),
                        }
                )
            );
        }

        if (await db.PointsGiveaways.AnyAsync(x => x.HostId == hostId, cancellationToken))
        {
            return;
        }

        db.PointsGiveaways.Add(
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
        await db.SaveChangesAsync(cancellationToken);

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
        db.CustomCommands.Add(
            new CustomCommand
            {
                HostId = hostId,
                Name = "Moderator-only fixture",
                Enabled = true,
                ModeratorOnly = true,
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
        db.CustomCommands.Add(
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
            db.CustomCommands.Add(
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

        db.CustomAnnouncements.Add(
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

        db.RequestBoards.Add(
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

        db.PlayQueues.Add(
            new PlayQueue
            {
                HostId = hostId,
                Slug = "main",
                Name = "Community night",
                ActivityName = "BlokeQuest",
                IsOpen = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
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
        db.MomentCandidates.Add(
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

        db.DurableAlerts.Add(
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
            db.AutomaticRaidShoutoutSettings.Add(
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
