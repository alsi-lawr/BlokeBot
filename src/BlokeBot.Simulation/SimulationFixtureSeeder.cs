using BlokeBot.Announcements;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Bingo;
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
    internal const string CommunityGoalOverlayAccessKey =
        "simulation-community-goal-key-0000000000000";
    internal const string ViewerFundedBountyOverlayAccessKey =
        "simulation-bounty-progress-key-000000000000";

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
        await SeedViewerPassportsAsync(db, hostId, now, cancellationToken);
        await SeedBingoAsync(db, hostId, now, cancellationToken);
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

    private static async Task SeedViewerPassportsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (await db.ViewerPassports.AnyAsync(value => value.HostId == hostId, cancellationToken))
        {
            return;
        }
        var titleId = await db
            .CommunityRewardDefinitions.Where(value =>
                value.HostId == hostId && value.Key == "trailblazer"
            )
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);
        var badgeId = await db
            .CommunityRewardDefinitions.Where(value =>
                value.HostId == hostId && value.Key == "summer-star"
            )
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);
        var streamer = new ViewerPassport
        {
            HostId = hostId,
            TwitchUserId = SimulationMode.UserId,
            Login = SimulationMode.Login,
            DisplayName = SimulationMode.DisplayName,
            ProfileLine = "Building a welcoming corner for cosy chaos and unlikely comebacks.",
            Visibility = ViewerPassportVisibility.Private,
            HideAttendance = false,
            CreatedAtUtc = now.AddDays(-20),
            UpdatedAtUtc = now.AddMinutes(-8),
        };
        var nightOwl = new ViewerPassport
        {
            HostId = hostId,
            TwitchUserId = "3000",
            Login = "nightowl",
            DisplayName = "NightOwl",
            ProfileLine = "Here for cosy chaos, unlikely comebacks, and a proper brew.",
            Visibility = ViewerPassportVisibility.Public,
            HideAttendance = false,
            SelectedTitleRewardDefinitionId = titleId,
            SelectedBadgeRewardDefinitionId = badgeId,
            CreatedAtUtc = now.AddDays(-18),
            UpdatedAtUtc = now.AddMinutes(-4),
        };
        db.ViewerPassports.AddRange(streamer, nightOwl);
        _ = await db.SaveChangesAsync(cancellationToken);
        db.ViewerPassportLogins.AddRange(
            new ViewerPassportLogin
            {
                HostId = hostId,
                PassportId = streamer.Id,
                Login = streamer.Login,
                FirstSeenAtUtc = streamer.CreatedAtUtc,
                LastSeenAtUtc = streamer.UpdatedAtUtc,
            },
            new ViewerPassportLogin
            {
                HostId = hostId,
                PassportId = nightOwl.Id,
                Login = nightOwl.Login,
                FirstSeenAtUtc = nightOwl.CreatedAtUtc,
                LastSeenAtUtc = nightOwl.UpdatedAtUtc,
            }
        );
        for (var offset = 0; offset < 6; offset++)
        {
            db.ViewerPassportAttendanceDays.AddRange(
                new ViewerPassportAttendanceDay
                {
                    HostId = hostId,
                    PassportId = streamer.Id,
                    DateUtc = DateOnly.FromDateTime(now.AddDays(-offset)),
                    FirstSeenAtUtc = now.AddDays(-offset).AddHours(-2),
                },
                new ViewerPassportAttendanceDay
                {
                    HostId = hostId,
                    PassportId = nightOwl.Id,
                    DateUtc = DateOnly.FromDateTime(now.AddDays(-offset)),
                    FirstSeenAtUtc = now.AddDays(-offset).AddHours(-3),
                }
            );
        }
    }

    private static async Task SeedBingoAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var templatePublicId = Guid.Parse("e234243b-ae40-47dd-bae3-e9e456369e91");
        if (
            await db.BingoTemplates.AnyAsync(
                value => value.PublicId == templatePublicId,
                cancellationToken
            )
        )
        {
            return;
        }

        var template = new BingoTemplate
        {
            HostId = hostId,
            PublicId = templatePublicId,
            CreationOperationId = Guid.Parse("6c098a89-ee48-49ec-87c2-a4ccb1f61724"),
            Name = "Tonight's stream moments",
            CurrentRevision = 3,
            CreatedAtUtc = now.AddDays(-14),
            UpdatedAtUtc = now.AddMinutes(-50),
        };
        var revision = new BingoTemplateRevision
        {
            HostId = hostId,
            OperationId = Guid.Parse("fe4c7846-ad3b-4d18-b725-ced8fb76619f"),
            Template = template,
            Revision = 3,
            Dimension = 4,
            FullCardWinEnabled = true,
            LinePointsReward = "250",
            LineAchievementKey = "bingo-winner",
            FullCardPointsReward = "1000",
            FullCardAchievementKey = "bingo-winner",
            CreatedByTwitchUserId = "1000",
            CreatedByLogin = "streamer",
            CreatedAtUtc = now.AddMinutes(-50),
        };
        var squareKinds = new[]
        {
            BingoSquareKind.Manual,
            BingoSquareKind.IncomingRaid,
            BingoSquareKind.BountyCompleted,
            BingoSquareKind.GuessingResult,
            BingoSquareKind.GiveawayStarted,
            BingoSquareKind.StreamCategoryChanged,
        };
        var titles = new[]
        {
            "Chat predicts the plot twist",
            "A raid brings the party",
            "Community bounty completed",
            "Blue wins the guessing round",
            "Giveaway opens",
            "Category changes to Just Chatting",
            "Streamer says 'one more try'",
            "Incoming raid with 10+ viewers",
            "No-winner guessing result",
            "Moderator calls a clutch save",
            "Giveaway opens after a win",
            "Second bounty completed",
            "Category changes mid-stream",
            "Chat spots a hidden detail",
            "A raid lands during the break",
            "Streamer thanks the team",
        };
        for (var index = 0; index < 16; index++)
        {
            var kind = squareKinds[index % squareKinds.Length];
            revision.Squares.Add(
                new BingoSquare
                {
                    HostId = hostId,
                    Key = $"moment-{index + 1}",
                    SortOrder = index,
                    Title = titles[index],
                    Kind = kind,
                    Threshold = kind == BingoSquareKind.IncomingRaid ? 10 : null,
                    FilterToken = kind switch
                    {
                        BingoSquareKind.GuessingResult => "blue",
                        BingoSquareKind.StreamCategoryChanged => "509658",
                        _ => null,
                    },
                    PrivateModeratorNote =
                        kind == BingoSquareKind.Manual
                            ? "Confirm only when the moment is clear on stream."
                            : string.Empty,
                }
            );
        }
        var archiveTemplate = new BingoTemplate
        {
            HostId = hostId,
            PublicId = Guid.Parse("95d4f8bb-2e3b-43dc-aabd-418f0498761b"),
            CreationOperationId = Guid.Parse("664461c4-cc46-4427-a905-86d5be1c8ca3"),
            Name = "Five-by-five stream archive",
            CurrentRevision = 1,
            CreatedAtUtc = now.AddDays(-4),
            UpdatedAtUtc = now.AddDays(-3),
        };
        var archiveRevision = new BingoTemplateRevision
        {
            HostId = hostId,
            OperationId = Guid.Parse("338c88dd-39b4-4fb9-9c97-52e4d43cc6b4"),
            Template = archiveTemplate,
            Revision = 1,
            Dimension = 5,
            FullCardWinEnabled = true,
            LinePointsReward = "250",
            LineAchievementKey = "bingo-winner",
            FullCardPointsReward = "1000",
            FullCardAchievementKey = "bingo-winner",
            CreatedByTwitchUserId = "1000",
            CreatedByLogin = "streamer",
            CreatedAtUtc = now.AddDays(-3),
        };
        for (var index = 0; index < 25; index++)
        {
            var kind = squareKinds[index % squareKinds.Length];
            archiveRevision.Squares.Add(
                new BingoSquare
                {
                    HostId = hostId,
                    Key = $"archive-moment-{index + 1}",
                    SortOrder = index,
                    Title = $"Archived stream moment {index + 1}",
                    Kind = kind,
                    Threshold = kind == BingoSquareKind.IncomingRaid ? 10 : null,
                    FilterToken = kind switch
                    {
                        BingoSquareKind.GuessingResult => "blue",
                        BingoSquareKind.StreamCategoryChanged => "509658",
                        _ => null,
                    },
                }
            );
        }
        var game = new BingoGame
        {
            HostId = hostId,
            PublicId = Guid.Parse("3201ca21-ef80-4b70-8558-f677474d58f3"),
            CreationOperationId = Guid.Parse("45942ff8-3f3e-4344-a7a1-82a7bc19e4c1"),
            TemplateRevision = revision,
            TemplateName = template.Name,
            TemplateRevisionNumber = 3,
            Dimension = 4,
            Seed = "neon-night-42",
            Mode = BingoGameMode.Team,
            Status = BingoGameStatus.Issued,
            ParticipantCap = 12,
            TeamCap = 2,
            FullCardWinEnabled = true,
            LinePointsReward = "250",
            LineAchievementKey = "bingo-winner",
            FullCardPointsReward = "1000",
            FullCardAchievementKey = "bingo-winner",
            CreatedAtUtc = now.AddMinutes(-45),
            IssuedAtUtc = now.AddMinutes(-35),
        };
        var aurora = new BingoTeam
        {
            HostId = hostId,
            Game = game,
            PublicId = Guid.Parse("425fd611-c9ea-4cff-aa71-2cfd33d290da"),
            Name = "Team Aurora",
            SortOrder = 0,
        };
        var nebula = new BingoTeam
        {
            HostId = hostId,
            Game = game,
            PublicId = Guid.Parse("edc6beea-e866-41d8-aebc-e9d4ec563950"),
            Name = "Team Nebula",
            SortOrder = 1,
        };
        var auroraCard = new BingoCard
        {
            HostId = hostId,
            Game = game,
            PublicId = Guid.Parse("caf9c96f-5326-45c0-9cb7-0849359d5bf9"),
            AssignmentKey = $"team:{aurora.PublicId:N}",
            AssignmentName = aurora.Name,
            IssuedAtUtc = now.AddMinutes(-35),
        };
        var nebulaCard = new BingoCard
        {
            HostId = hostId,
            Game = game,
            PublicId = Guid.Parse("249491e1-50b8-4ef9-bdba-30a2c03fb8a7"),
            AssignmentKey = $"team:{nebula.PublicId:N}",
            AssignmentName = nebula.Name,
            IssuedAtUtc = now.AddMinutes(-35),
        };
        var participants = new[]
        {
            Participant(hostId, game, aurora, auroraCard, "3000", "nightowl", "NightOwl", now),
            Participant(hostId, game, aurora, auroraCard, "2002", "pixelpilot", "PixelPilot", now),
            Participant(hostId, game, nebula, nebulaCard, "2003", "cozycactus", "CozyCactus", now),
        };
        var archivedGame = new BingoGame
        {
            HostId = hostId,
            PublicId = Guid.Parse("69e9e686-06cc-4879-ae59-fd0279f1d820"),
            CreationOperationId = Guid.Parse("fe7fc0cb-2e5b-4199-a967-22a05d7dc271"),
            TemplateRevision = archiveRevision,
            TemplateName = archiveTemplate.Name,
            TemplateRevisionNumber = 1,
            Dimension = 5,
            Seed = "archive-night-17",
            Mode = BingoGameMode.Shared,
            Status = BingoGameStatus.Archived,
            FullCardWinEnabled = true,
            LinePointsReward = "250",
            LineAchievementKey = "bingo-winner",
            FullCardPointsReward = "1000",
            FullCardAchievementKey = "bingo-winner",
            CreatedAtUtc = now.AddDays(-3),
            IssuedAtUtc = now.AddDays(-3).AddMinutes(10),
            CompletedAtUtc = now.AddDays(-3).AddHours(2),
            ArchivedAtUtc = now.AddDays(-3).AddHours(3),
        };
        var archivedCard = new BingoCard
        {
            HostId = hostId,
            Game = archivedGame,
            PublicId = Guid.Parse("4a9676d5-da3f-4edc-bd79-83a2eb2bc557"),
            AssignmentKey = "shared",
            AssignmentName = "Everyone",
            IssuedAtUtc = now.AddDays(-3).AddMinutes(10),
        };
        var archivedParticipant = new BingoParticipant
        {
            HostId = hostId,
            Game = archivedGame,
            Card = archivedCard,
            TwitchUserId = "2004",
            Login = "archivist",
            DisplayName = "Archivist",
            JoinedAtUtc = now.AddDays(-3).AddMinutes(5),
        };
        db.BingoTemplates.AddRange(template, archiveTemplate);
        db.BingoGames.AddRange(game, archivedGame);
        db.BingoTeams.AddRange(aurora, nebula);
        db.BingoCards.AddRange(auroraCard, nebulaCard, archivedCard);
        db.BingoParticipants.AddRange(participants);
        _ = db.BingoParticipants.Add(archivedParticipant);
        _ = await db.SaveChangesAsync(cancellationToken);

        var layout = BingoCardLayout.Generate(
            game.Seed,
            game.TemplateRevisionNumber,
            new(game.Dimension),
            auroraCard.AssignmentKey,
            revision.Squares.Select(value => new BingoSquareKey(value.Key))
        );
        var summaries = new[]
        {
            "Moderator confirmed this square",
            "Incoming raid from @friendlyraider with 42 viewers",
            "Bounty completed",
            "Guessing result: Blue",
        };
        for (var position = 0; position < 4; position++)
        {
            var key = layout[position].Value;
            var definition = revision.Squares.Single(value => value.Key == key);
            var mark = new BingoMark
            {
                HostId = hostId,
                GameId = game.Id,
                CardId = auroraCard.Id,
                SquareKey = key,
                Position = position,
                IsActive = position != 2,
                FirstMarkedAtUtc = now.AddMinutes(-25 + position),
                ChangedAtUtc = now.AddMinutes(-10 + position),
            };
            mark.Evidence.Add(
                new BingoEvidence
                {
                    HostId = hostId,
                    GameId = game.Id,
                    CardId = auroraCard.Id,
                    Action = BingoEvidenceAction.Marked,
                    Source =
                        definition.Kind == BingoSquareKind.Manual
                            ? BingoEvidenceSource.Manual
                            : BingoEvidenceSource.Automatic,
                    EventKind = definition.Kind,
                    Summary = summaries[position],
                    ParticipantTwitchUserId = position == 1 ? "raid-42" : null,
                    ParticipantLogin = position == 1 ? "friendlyraider" : null,
                    ParticipantDisplayName = position == 1 ? "FriendlyRaider" : null,
                    OccurredAtUtc = now.AddMinutes(-25 + position),
                    RecordedAtUtc = now.AddMinutes(-25 + position),
                }
            );
            if (position == 2)
            {
                mark.Evidence.Add(
                    new BingoEvidence
                    {
                        HostId = hostId,
                        GameId = game.Id,
                        CardId = auroraCard.Id,
                        Action = BingoEvidenceAction.Reversed,
                        Source = BingoEvidenceSource.Manual,
                        EventKind = definition.Kind,
                        Summary = "Moderator reversed this square",
                        OccurredAtUtc = now.AddMinutes(-10),
                        RecordedAtUtc = now.AddMinutes(-10),
                    }
                );
            }
            _ = db.BingoMarks.Add(mark);
        }
        var win = new BingoWin
        {
            HostId = hostId,
            GameId = game.Id,
            CardId = auroraCard.Id,
            PublicId = Guid.Parse("fc68be0e-8e72-407c-9798-46614dcdbabf"),
            Kind = BingoWinKind.Row,
            RuleIndex = 0,
            RuleKey = "row:0",
            PointsReward = "250",
            AchievementKey = "bingo-winner",
            CompletedAtUtc = now.AddMinutes(-20),
            RewardsCompletedAtUtc = now.AddMinutes(-20),
        };
        foreach (var participant in participants.Where(value => value.Team == aurora))
        {
            win.Recipients.Add(
                new BingoWinRecipient
                {
                    HostId = hostId,
                    TwitchUserId = participant.TwitchUserId,
                    Login = participant.Login,
                    DisplayName = participant.DisplayName,
                    PointsGranted = true,
                    AchievementGranted = true,
                }
            );
        }
        _ = db.BingoWins.Add(win);
        _ = db.BingoModerationAudit.Add(
            new BingoModerationAudit
            {
                HostId = hostId,
                GameId = game.Id,
                OperationId = Guid.Parse("446134d1-cf56-475a-9ef2-c548090e52e0"),
                Action = "reverse",
                ActorTwitchUserId = "1000",
                ActorLogin = "streamer",
                PrivateNote =
                    "Corrected after reviewing the moment; the earlier win remains rewarded.",
                OccurredAtUtc = now.AddMinutes(-10),
            }
        );
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private static BingoParticipant Participant(
        int hostId,
        BingoGame game,
        BingoTeam team,
        BingoCard card,
        string twitchUserId,
        string login,
        string displayName,
        DateTime now
    ) =>
        new()
        {
            HostId = hostId,
            Game = game,
            Team = team,
            Card = card,
            TwitchUserId = twitchUserId,
            Login = login,
            DisplayName = displayName,
            JoinedAtUtc = now.AddMinutes(-40),
        };

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
        var bingoTitle = new CommunityRewardDefinition
        {
            PublicId = Guid.Parse("9a099c12-28ed-4ca0-8ba6-fc51ef37521d"),
            HostId = hostId,
            Key = "bingo-caller",
            Kind = CommunityRewardKind.Title,
            Name = "Bingo caller",
            PresentationToken = "bingo-caller",
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
        var bingoAchievement = new CommunityDefinition
        {
            PublicId = Guid.Parse("9cb52fb5-5445-4332-8fe9-c49eadde947c"),
            HostId = hostId,
            Key = "bingo-winner",
            Name = "Bingo winner",
            Description = "Complete a configured Bingo win rule.",
            Kind = CommunityDefinitionKind.Achievement,
            Scope = CommunityProgressScope.Viewer,
            CompletionMode = CommunityCompletionMode.OneTime,
            EventRule = CommunityEventRuleKind.ExternalGrant,
            Increment = CommunityProgressIncrement.Occurrence,
            Target = 1,
            PointsReward = "0",
            ResetCadence = CommunityResetCadence.None,
            ResetLocalTime = "00:00",
            ScheduleRevision = 1,
            CreatedAtUtc = now.AddDays(-6),
            Season = season,
        };
        var communal = new CommunityDefinition
        {
            PublicId = Guid.Parse("bcd1434c-16c9-4d29-83cb-a2ceeffb0e22"),
            HostId = hostId,
            Key = "community-bounty-drive",
            Name = "Community bounty drive",
            Description = "Complete four channel bounties together.",
            Kind = CommunityDefinitionKind.Quest,
            Scope = CommunityProgressScope.Communal,
            CompletionMode = CommunityCompletionMode.Repeatable,
            EventRule = CommunityEventRuleKind.BountyCompleted,
            Increment = CommunityProgressIncrement.Occurrence,
            Target = 4,
            PointsReward = "0",
            ResetCadence = CommunityResetCadence.None,
            ResetLocalTime = "00:00",
            ScheduleRevision = 1,
            CreatedAtUtc = now.AddDays(-6),
            Season = season,
        };
        _ = db.CommunitySeasons.Add(season);
        db.CommunityRewardDefinitions.AddRange(title, badge, bingoTitle);
        db.CommunityDefinitions.AddRange(achievement, daily, communal, bingoAchievement);
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
            },
            new CommunityDefinitionReward
            {
                DefinitionId = bingoAchievement.Id,
                RewardDefinitionId = bingoTitle.Id,
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
            },
            new CommunityProgress
            {
                HostId = hostId,
                SeasonId = season.Id,
                DefinitionId = communal.Id,
                SubjectKey = "communal",
                Amount = 3,
                CompletionCount = 1,
                UpdatedAtUtc = now.AddMinutes(-6),
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
        _ = await db.SaveChangesAsync(cancellationToken);
        await SeedArchivedCommunitySeasonAsync(db, hostId, now, cancellationToken);
        await SeedHiddenCommunitySeasonAsync(db, hostId, now, cancellationToken);
    }

    private static async Task SeedArchivedCommunitySeasonAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var season = new CommunitySeason
        {
            PublicId = Guid.Parse("b24a1144-4bd0-43b2-b71e-5f4043c92bb8"),
            HostId = hostId,
            CreationOperationId = Guid.Parse("6a26aa7b-0779-47cd-8024-49c135397785"),
            Name = "Spring launch legacy",
            Description = "Archived standings from the channel launch season.",
            ModeratorNotes = "Archived simulation note",
            Status = CommunitySeasonStatus.Archived,
            Visibility = CommunityVisibility.Public,
            StartsAtUtc = now.AddDays(-90),
            EndsAtUtc = now.AddDays(-60),
            OpenedAtUtc = now.AddDays(-90),
            ClosedAtUtc = now.AddDays(-60),
            ArchivedAtUtc = now.AddDays(-45),
            Revision = 4,
            CreatedAtUtc = now.AddDays(-91),
            UpdatedAtUtc = now.AddDays(-45),
        };
        var achievement = new CommunityDefinition
        {
            PublicId = Guid.Parse("217fc626-f6c2-470a-a0dd-1aebbc23053d"),
            HostId = hostId,
            Key = "launch-regular",
            Name = "Launch regular",
            Description = "Joined the launch season.",
            Kind = CommunityDefinitionKind.Achievement,
            Scope = CommunityProgressScope.Viewer,
            CompletionMode = CommunityCompletionMode.OneTime,
            EventRule = CommunityEventRuleKind.ChatMessage,
            Increment = CommunityProgressIncrement.Occurrence,
            Target = 1,
            PointsReward = "0",
            ResetCadence = CommunityResetCadence.None,
            ResetLocalTime = "00:00",
            ScheduleRevision = 1,
            CreatedAtUtc = now.AddDays(-91),
            Season = season,
        };
        _ = db.CommunitySeasons.Add(season);
        _ = db.CommunityDefinitions.Add(achievement);
        _ = await db.SaveChangesAsync(cancellationToken);
        _ = db.CommunityProgress.Add(
            new()
            {
                HostId = hostId,
                SeasonId = season.Id,
                DefinitionId = achievement.Id,
                SubjectKey = "viewer:simulation-chatregular-id",
                ViewerTwitchUserId = "simulation-chatregular-id",
                ViewerLogin = "chatregular",
                ViewerDisplayName = "ChatRegular",
                Amount = 1,
                CompletionCount = 1,
                UpdatedAtUtc = now.AddDays(-70),
            }
        );
        _ = db.CommunityCompletions.Add(
            new()
            {
                PublicId = Guid.Parse("41557a98-cebd-4c0d-b738-9582b37e1bc8"),
                HostId = hostId,
                SeasonId = season.Id,
                DefinitionId = achievement.Id,
                SubjectKey = "viewer:simulation-chatregular-id",
                ViewerTwitchUserId = "simulation-chatregular-id",
                ViewerLogin = "chatregular",
                ViewerDisplayName = "ChatRegular",
                DefinitionKey = achievement.Key,
                DefinitionName = achievement.Name,
                Sequence = 1,
                PointsGranted = "0",
                RewardSnapshot = "[]",
                SourceOperationKey = "simulation-archived-chat",
                CompletedAtUtc = now.AddDays(-70),
            }
        );
        _ = db.CommunitySeasonStandings.Add(
            new()
            {
                HostId = hostId,
                SeasonId = season.Id,
                ViewerTwitchUserId = "simulation-chatregular-id",
                ViewerLogin = "chatregular",
                ViewerDisplayName = "ChatRegular",
                CompletedCount = 1,
                ProgressAmount = 1,
                Rank = 1,
                SnapshottedAtUtc = now.AddDays(-60),
            }
        );
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedHiddenCommunitySeasonAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var season = new CommunitySeason
        {
            PublicId = Guid.Parse("1d528657-799a-4340-ad28-960a13e79fca"),
            HostId = hostId,
            CreationOperationId = Guid.Parse("3841c2ea-e038-476f-be28-a2dd43bd40de"),
            Name = "Moderator-only surprise season",
            Description = "A hidden progression workspace for moderators.",
            ModeratorNotes = "Never public simulation material",
            Status = CommunitySeasonStatus.Open,
            Visibility = CommunityVisibility.Hidden,
            StartsAtUtc = now.AddDays(-4),
            EndsAtUtc = now.AddDays(20),
            OpenedAtUtc = now.AddDays(-4),
            Revision = 2,
            CreatedAtUtc = now.AddDays(-7),
            UpdatedAtUtc = now.AddMinutes(-3),
        };
        var communal = new CommunityDefinition
        {
            PublicId = Guid.Parse("f0fd3b15-aafb-4dc4-aa86-76d8f33f26f8"),
            HostId = hostId,
            Key = "secret-channel-goal",
            Name = "Secret channel goal",
            Description = "Hidden communal progress.",
            Kind = CommunityDefinitionKind.Quest,
            Scope = CommunityProgressScope.Communal,
            CompletionMode = CommunityCompletionMode.Repeatable,
            EventRule = CommunityEventRuleKind.BountyCompleted,
            Increment = CommunityProgressIncrement.Occurrence,
            Target = 10,
            PointsReward = "0",
            ResetCadence = CommunityResetCadence.None,
            ResetLocalTime = "00:00",
            ScheduleRevision = 1,
            CreatedAtUtc = now.AddDays(-7),
            Season = season,
        };
        _ = db.CommunitySeasons.Add(season);
        _ = db.CommunityDefinitions.Add(communal);
        _ = await db.SaveChangesAsync(cancellationToken);
        _ = db.CommunityProgress.Add(
            new()
            {
                HostId = hostId,
                SeasonId = season.Id,
                DefinitionId = communal.Id,
                SubjectKey = "communal",
                Amount = 7,
                CompletionCount = 0,
                UpdatedAtUtc = now.AddMinutes(-3),
            }
        );
        _ = await db.SaveChangesAsync(cancellationToken);
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
            !await db.OverlayInstances.AnyAsync(
                value => value.PublicId == Guid.Parse("4c067d9e-5f8f-4b98-b9eb-597dc34f70fa"),
                cancellationToken
            )
        )
        {
            _ = db.OverlayInstances.Add(
                new OverlayInstance
                {
                    PublicId = Guid.Parse("4c067d9e-5f8f-4b98-b9eb-597dc34f70fa"),
                    HostId = hostId,
                    Name = "Community milestone",
                    Type = OverlayType.CommunityGoal,
                    IsEnabled = true,
                    ConfigurationJson =
                        """{"schemaVersion":1,"selectedItemId":null,"rotationSeconds":20,"recentContributorCount":0,"appearance":{"x":1160,"y":80,"width":680,"height":300,"css":""}}""",
                    AccessKeyDigest = OverlayAccessKeyDigest.Compute(CommunityGoalOverlayAccessKey),
                    KeyVersion = 1,
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
        }

        if (
            !await db.OverlayInstances.AnyAsync(
                value => value.PublicId == Guid.Parse("b1396f64-e28e-44df-8eaa-b1fb2ac0ff26"),
                cancellationToken
            )
        )
        {
            _ = db.OverlayInstances.Add(
                new OverlayInstance
                {
                    PublicId = Guid.Parse("b1396f64-e28e-44df-8eaa-b1fb2ac0ff26"),
                    HostId = hostId,
                    Name = "Viewer challenge",
                    Type = OverlayType.ViewerFundedBounty,
                    IsEnabled = true,
                    ConfigurationJson =
                        """{"schemaVersion":1,"selectedItemId":null,"rotationSeconds":20,"recentContributorCount":3,"appearance":{"x":1160,"y":80,"width":680,"height":340,"css":""}}""",
                    AccessKeyDigest = OverlayAccessKeyDigest.Compute(
                        ViewerFundedBountyOverlayAccessKey
                    ),
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
