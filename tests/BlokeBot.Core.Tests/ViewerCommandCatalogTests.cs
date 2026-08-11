using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Core.Hosts;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class ViewerCommandCatalogTests
{
    [Test]
    public async Task ViewerPassportsSwitch_LoadingViewerCatalog_OmitsOwnedCommandWhileOff()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.ViewerPassports,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline()),
            new RecordingCueAdmissions(),
            new UnavailableCustomCommandAutomationRuntime()
        );

        var enabled = await catalog.LoadForHostAsync(hostId, default);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await db.SaveChangesAsync();
        }
        var disabled = await catalog.LoadForHostAsync(hostId, default);

        enabled.Names.ShouldContain("!passport");
        disabled.Names.ShouldNotContain("!passport");
    }

    [Test]
    public async Task CompetitionSwitch_LoadingViewerCatalog_OmitsOwnedCommandsWhileOff()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.Competitions,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            _ = db.Competitions.Add(
                new Competition
                {
                    HostId = hostId,
                    PublicId = Guid.NewGuid(),
                    CreationOperationId = Guid.NewGuid(),
                    Name = "Competition",
                    Format = CompetitionFormat.RoundRobin,
                    EntryKind = CompetitionEntryKind.Individual,
                    Status = CompetitionStatus.Registration,
                    Seeding = CompetitionSeeding.Random,
                    Tiebreak = CompetitionTiebreak.ScoreDifferenceThenScoreFor,
                    Capacity = 8,
                    TeamSize = 1,
                    Seed = "seed",
                    AlgorithmVersion = "blokebot-shuffle-v1",
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline()),
            new RecordingCueAdmissions(),
            new UnavailableCustomCommandAutomationRuntime()
        );

        var enabled = await catalog.LoadForHostAsync(hostId, default);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await db.SaveChangesAsync();
        }
        var disabled = await catalog.LoadForHostAsync(hostId, default);

        enabled.Names.ShouldContain("!competitions");
        enabled.Names.ShouldContain("!competitionjoin");
        disabled.Names.ShouldNotContain("!competitions");
        disabled.Names.ShouldNotContain("!competitionjoin");
    }

    [Test]
    public async Task BingoSwitch_LoadingViewerCatalog_OmitsOwnedCommandsWhileOff()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.Bingo,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            var template = new BingoTemplate
            {
                HostId = hostId,
                PublicId = Guid.NewGuid(),
                CreationOperationId = Guid.NewGuid(),
                Name = "Bingo",
                CurrentRevision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            var revision = new BingoTemplateRevision
            {
                HostId = hostId,
                OperationId = Guid.NewGuid(),
                Template = template,
                Revision = 1,
                Dimension = 3,
                LinePointsReward = "0",
                FullCardPointsReward = "0",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.BingoGames.Add(
                new BingoGame
                {
                    HostId = hostId,
                    PublicId = Guid.NewGuid(),
                    CreationOperationId = Guid.NewGuid(),
                    TemplateRevision = revision,
                    TemplateName = "Bingo",
                    TemplateRevisionNumber = 1,
                    Dimension = 3,
                    Seed = "seed",
                    Mode = BingoGameMode.Shared,
                    Status = BingoGameStatus.Joining,
                    LinePointsReward = "0",
                    FullCardPointsReward = "0",
                    CreatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline()),
            new RecordingCueAdmissions(),
            new UnavailableCustomCommandAutomationRuntime()
        );

        var enabled = await catalog.LoadForHostAsync(hostId, default);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await db.SaveChangesAsync();
        }
        var disabled = await catalog.LoadForHostAsync(hostId, default);

        enabled.Names.ShouldContain("!bingo");
        enabled.Names.ShouldContain("!bingojoin");
        disabled.Names.ShouldNotContain("!bingo");
        disabled.Names.ShouldNotContain("!bingojoin");
    }

    [Test]
    public async Task CommunityProgressionSwitch_LoadingViewerCatalog_OmitsOwnedCommandsWhileOff()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.CommunityProgression,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline()),
            new RecordingCueAdmissions(),
            new UnavailableCustomCommandAutomationRuntime()
        );

        var enabled = await catalog.LoadForHostAsync(hostId, CancellationToken.None);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await db.SaveChangesAsync();
        }
        var disabled = await catalog.LoadForHostAsync(hostId, CancellationToken.None);

        enabled.Names.ShouldContain("!progress");
        enabled.Names.ShouldContain("!equiptitle");
        disabled.Names.ShouldNotContain("!progress");
        disabled.Names.ShouldNotContain("!equiptitle");
    }

    [Test]
    public async Task OverlayCueCustomCommand_LoadingCatalog_InheritsOverlaysWithoutHidingMessageCommands()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        var targetId = Guid.NewGuid();
        var cueId = Guid.NewGuid();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.CustomCommands,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            db.CustomCommands.AddRange(
                CatalogCommand(
                    hostId,
                    "message",
                    new MessageCustomCommandAction { HostId = hostId }
                ),
                CatalogCommand(
                    hostId,
                    "cue",
                    new OverlayCueCustomCommandAction
                    {
                        HostId = hostId,
                        TargetOverlayPublicId = targetId,
                        CuePublicId = cueId,
                        QueuePolicy = OverlayCueQueuePolicy.Enqueue,
                        ReplyOrder = OverlayCueReplyOrder.After,
                    }
                )
            );
            _ = db.OverlayInstances.Add(
                new OverlayInstance
                {
                    HostId = hostId,
                    PublicId = targetId,
                    Name = "Player",
                    Type = OverlayType.CuePlayer,
                    IsEnabled = true,
                    ConfigurationJson = """{"schemaVersion":1}""",
                    AccessKeyDigest = System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes("catalog-player")
                    ),
                    KeyVersion = 1,
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = db.OverlayCues.Add(
                new OverlayCue
                {
                    HostId = hostId,
                    PublicId = cueId,
                    Name = "Cue",
                    IsEnabled = true,
                    DurationMilliseconds = 1000,
                    QueuePolicy = OverlayCueQueuePolicy.Enqueue,
                    ConfigurationJson = """{"schemaVersion":1,"layers":[]}""",
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var references = new RecordingCueAdmissions
        {
            Outcome = new OverlayCueReferenceOutcome.Disabled(OverlayCueReferencePart.Parent),
        };
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline()),
            references,
            new UnavailableCustomCommandAutomationRuntime()
        );

        var overlaysOff = await catalog.LoadForHostAsync(hostId, CancellationToken.None);
        overlaysOff.Names.ShouldBe(["!cue", "!message"]);
        overlaysOff
            .Entries.Single(entry => entry.Name == "!cue")
            .Availability.ShouldBe(ViewerCommandCatalogAvailability.ActionUnavailable);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
            host.EnabledFeatures |= HostFeatureFlags.Overlays;
            _ = await db.SaveChangesAsync();
        }
        references.Outcome = new OverlayCueReferenceOutcome.Available();

        var overlaysOn = await catalog.LoadForHostAsync(hostId, CancellationToken.None);
        overlaysOn.Names.ShouldBe(["!cue", "!message"]);
        overlaysOn
            .Entries.Single(entry => entry.Name == "!cue")
            .Availability.ShouldBe(ViewerCommandCatalogAvailability.Available);
        references.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task ChannelState_LoadingCatalog_ListsViewerCanonicalRoutesOnceInStableOrder()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Live("stream")),
            new RecordingCueAdmissions(),
            new UnavailableCustomCommandAutomationRuntime()
        );

        var snapshot = await catalog.LoadForHostAsync(fixture.HostId, CancellationToken.None);

        snapshot.Names.ShouldBe(snapshot.Names.Order(StringComparer.OrdinalIgnoreCase));
        snapshot.Names.ShouldNotContain("!alpha");
        snapshot
            .Names.Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ShouldBe(snapshot.Names.Count);
        snapshot.Conflicts.ShouldBeEmpty();
    }

    [Test]
    public async Task BountyRoutes_LoadingViewerCatalog_RequireBothSwitchesAndPublicState()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline()),
            new RecordingCueAdmissions(),
            new UnavailableCustomCommandAutomationRuntime()
        );

        var enabledEmpty = await catalog.LoadForHostAsync(fixture.HostId, default);
        enabledEmpty.Names.ShouldContain("!bounties");
        enabledEmpty.Names.ShouldNotContain("!bounty");
        enabledEmpty.Names.ShouldNotContain("!bountypledge");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var now = DateTime.UtcNow;
            _ = db.Bounties.Add(
                new Bounty
                {
                    HostId = fixture.HostId,
                    PublicId = Guid.NewGuid(),
                    CreationOperationId = Guid.NewGuid(),
                    Title = "Public",
                    Status = BountyStatus.Funding,
                    Visibility = BountyVisibility.Public,
                    FailurePledgePolicy = BountyFailurePledgePolicy.Refund,
                    RewardDistribution = BountyRewardDistribution.Equal,
                    FundingTarget = "100",
                    PledgedAmount = "0",
                    CompletionReward = "0",
                    ExpiresAtUtc = now.AddDays(1),
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var active = await catalog.LoadForHostAsync(fixture.HostId, default);
        active.Names.ShouldContain("!bounties");
        active.Names.ShouldContain("!bounty");
        active.Names.ShouldContain("!bountypledge");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync();
            host.EnabledFeatures &= ~HostFeatureFlags.Points;
            _ = await db.SaveChangesAsync();
        }

        var disabled = await catalog.LoadForHostAsync(fixture.HostId, default);
        disabled.Names.ShouldNotContain("!bounties");
        disabled.Names.ShouldNotContain("!bounty");
        disabled.Names.ShouldNotContain("!bountypledge");
    }

    [Test]
    public async Task CallerAccess_LoadingCatalog_HidesRestrictedCanonicalNamesFromOtherViewers()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.CustomCommands.Add(
                new CustomCommand
                {
                    HostId = fixture.HostId,
                    Name = "Selected",
                    Enabled = true,
                    AllowEveryone = false,
                    Aliases =
                    [
                        new CustomCommandAlias { HostId = fixture.HostId, Alias = "selected" },
                    ],
                    AllowedUsers =
                    [
                        new CustomCommandAllowedUser
                        {
                            HostId = fixture.HostId,
                            TwitchUserId = "selected-id",
                            Login = "old_login",
                            DisplayName = "Old name",
                        },
                    ],
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline()),
            new RecordingCueAdmissions(),
            new UnavailableCustomCommandAutomationRuntime()
        );

        var owner = await catalog.LoadForHostAsync(fixture.HostId, CancellationToken.None);
        var viewer = await catalog.LoadForViewerAsync(
            "streamer",
            Message("viewer", "viewer-id"),
            CancellationToken.None
        );
        var moderator = await catalog.LoadForViewerAsync(
            "streamer",
            Message("moderator", "moderator-id", moderator: true),
            CancellationToken.None
        );
        var selected = await catalog.LoadForViewerAsync(
            "streamer",
            Message("renamed_login", "selected-id"),
            CancellationToken.None
        );

        owner.Names.ShouldContain("!secret");
        owner.Names.ShouldContain("!selected");
        owner.Entries.Single(entry => entry.Name == "!secret").AccessSummary.ShouldBe("Moderators");
        owner
            .Entries.Single(entry => entry.Name == "!selected")
            .AccessSummary.ShouldBe("1 selected person");
        owner.Entries.Single(entry => entry.Name == "!zeta").AccessSummary.ShouldBe("Everyone");
        viewer.Names.ShouldNotContain("!secret");
        viewer.Names.ShouldNotContain("!selected");
        moderator.Names.ShouldContain("!secret");
        moderator.Names.ShouldNotContain("!selected");
        selected.Names.ShouldNotContain("!secret");
        selected.Names.ShouldContain("!selected");
        viewer.Names.ShouldContain("!zeta");
        moderator.Names.ShouldContain("!zeta");
        selected.Names.ShouldContain("!zeta");
    }

    [Test]
    public async Task OwnerInventory_LoadingCatalog_ListsDisabledAndUnavailableWithoutViewerDisclosure()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        var targetId = Guid.NewGuid();
        var cueId = Guid.NewGuid();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var disabled = CatalogCommand(
                fixture.HostId,
                "disabled",
                new MessageCustomCommandAction { HostId = fixture.HostId }
            );
            disabled.Enabled = false;
            disabled.AllowEveryone = false;
            disabled.AllowModerators = true;
            var unavailable = CatalogCommand(
                fixture.HostId,
                "unavailable",
                new OverlayCueCustomCommandAction
                {
                    HostId = fixture.HostId,
                    TargetOverlayPublicId = targetId,
                    CuePublicId = cueId,
                    QueuePolicy = OverlayCueQueuePolicy.Enqueue,
                    ReplyOrder = OverlayCueReplyOrder.After,
                }
            );
            db.CustomCommands.AddRange(disabled, unavailable);
            _ = await db.SaveChangesAsync();
        }
        var references = new RecordingCueAdmissions
        {
            Outcome = new OverlayCueReferenceOutcome.Disabled(OverlayCueReferencePart.Target),
        };
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline()),
            references,
            new UnavailableCustomCommandAutomationRuntime()
        );

        var owner = await catalog.LoadForHostAsync(fixture.HostId, CancellationToken.None);
        var viewer = await catalog.LoadForViewerAsync(
            "streamer",
            Message("viewer", "viewer-id"),
            CancellationToken.None
        );

        var disabledEntry = owner.Entries.Single(entry => entry.Name == "!disabled");
        disabledEntry.AccessSummary.ShouldBe("Moderators");
        disabledEntry.Availability.ShouldBe(ViewerCommandCatalogAvailability.TurnedOff);
        var unavailableEntry = owner.Entries.Single(entry => entry.Name == "!unavailable");
        unavailableEntry.AccessSummary.ShouldBe("Everyone");
        unavailableEntry.Availability.ShouldBe(ViewerCommandCatalogAvailability.ActionUnavailable);
        viewer.Names.ShouldNotContain("!disabled");
        viewer.Names.ShouldNotContain("!unavailable");
        viewer.Names.ShouldNotContain("!secret");
        viewer.Conflicts.ShouldAllBe(static message =>
            !message.Contains("disabled", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("secret", StringComparison.OrdinalIgnoreCase)
        );
        viewer.UnavailableFeatures.ShouldAllBe(static message =>
            !message.Contains("disabled", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("secret", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Test]
    public async Task AutomationCommand_LoadingCatalog_RequiresBothParentsAndAnEnabledSourceFlow()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        int commandId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var command = CatalogCommand(
                fixture.HostId,
                "automate",
                new AutomationCustomCommandAction { HostId = fixture.HostId }
            );
            _ = db.CustomCommands.Add(command);
            _ = await db.SaveChangesAsync();
            commandId = command.Id;
        }
        var automations = new CatalogAutomationRuntime(new HashSet<int> { commandId });
        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline()),
            new RecordingCueAdmissions(),
            automations
        );

        var availableOwner = await catalog.LoadForHostAsync(fixture.HostId, CancellationToken.None);
        var availableViewer = await catalog.LoadForViewerAsync(
            "streamer",
            Message("viewer", "viewer-id"),
            CancellationToken.None
        );
        availableOwner
            .Entries.Single(entry => entry.Name == "!automate")
            .Availability.ShouldBe(ViewerCommandCatalogAvailability.Available);
        availableViewer.Names.ShouldContain("!automate");

        await SetFeaturesAsync(
            dbFactory,
            fixture.HostId,
            HostFeatureFlags.All & ~HostFeatureFlags.Automations
        );
        var automationOffOwner = await catalog.LoadForHostAsync(
            fixture.HostId,
            CancellationToken.None
        );
        var automationOffViewer = await catalog.LoadForViewerAsync(
            "streamer",
            Message("viewer", "viewer-id"),
            CancellationToken.None
        );
        automationOffOwner
            .Entries.Single(entry => entry.Name == "!automate")
            .Availability.ShouldBe(ViewerCommandCatalogAvailability.ActionUnavailable);
        automationOffViewer.Names.ShouldNotContain("!automate");

        await SetFeaturesAsync(
            dbFactory,
            fixture.HostId,
            HostFeatureFlags.All & ~HostFeatureFlags.CustomCommands
        );
        var commandsOffOwner = await catalog.LoadForHostAsync(
            fixture.HostId,
            CancellationToken.None
        );
        var commandsOffViewer = await catalog.LoadForViewerAsync(
            "streamer",
            Message("viewer", "viewer-id"),
            CancellationToken.None
        );
        commandsOffOwner
            .Entries.Single(entry => entry.Name == "!automate")
            .Availability.ShouldBe(ViewerCommandCatalogAvailability.TurnedOff);
        commandsOffViewer.Names.ShouldNotContain("!automate");

        await SetFeaturesAsync(dbFactory, fixture.HostId, HostFeatureFlags.All);
        automations.AvailableCommandIds = new HashSet<int>();
        var noFlowOwner = await catalog.LoadForHostAsync(fixture.HostId, CancellationToken.None);
        var noFlowViewer = await catalog.LoadForViewerAsync(
            "streamer",
            Message("viewer", "viewer-id"),
            CancellationToken.None
        );
        noFlowOwner
            .Entries.Single(entry => entry.Name == "!automate")
            .Availability.ShouldBe(ViewerCommandCatalogAvailability.ActionUnavailable);
        noFlowViewer.Names.ShouldNotContain("!automate");
    }

    private static CustomCommand CatalogCommand(
        int hostId,
        string alias,
        CustomCommandAction action
    ) =>
        new()
        {
            HostId = hostId,
            Name = alias,
            Enabled = true,
            Action = action,
            Aliases = [new CustomCommandAlias { HostId = hostId, Alias = alias }],
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    [Test]
    public async Task ClosedRoundAndOffline_LoadingCatalog_UsesChannelWideAvailabilityOnly()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var round = await db.Rounds.SingleAsync(value => value.HostId == fixture.HostId);
            round.Status = GuessRoundStatus.Closed;
            round.ClosedAtUtc = DateTime.UtcNow;
            var giveaway = await db.PointsGiveaways.SingleAsync(value =>
                value.HostId == fixture.HostId
            );
            giveaway.Status = PointsGiveawayStatus.Completed;
            var board = await db.RequestBoards.SingleAsync(value => value.HostId == fixture.HostId);
            board.IsOpen = false;
            var queue = await db.PlayQueues.SingleAsync(value => value.HostId == fixture.HostId);
            queue.IsOpen = false;
            _ = await db.SaveChangesAsync();
        }

        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline()),
            new RecordingCueAdmissions(),
            new UnavailableCustomCommandAutomationRuntime()
        );
        var snapshot = await catalog.LoadForHostAsync(fixture.HostId, CancellationToken.None);

        snapshot.Names.ShouldNotContain("!predict");
        snapshot.Names.ShouldNotContain("!choices");
        snapshot.Names.ShouldNotContain("!enter");
        snapshot.Names.ShouldNotContain("!request");
        snapshot.Names.ShouldNotContain("!join");
        snapshot.Names.ShouldNotContain("!moment");
        snapshot.Names.ShouldNotContain("!clip");
        snapshot.Names.ShouldContain("!requests");
        snapshot.Names.ShouldContain("!requestvote");
        snapshot.Names.ShouldContain("!queue");
        snapshot.Names.ShouldContain("!leave");
        snapshot.Names.ShouldContain("!position");
        snapshot.Names.ShouldContain("!ready");
    }

    [Test]
    public async Task LegacyFixedRouteShadow_LoadingOwnerInventory_ListsUnavailableAndReportsConflict()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var custom = await db
                .CustomCommands.Include(static value => value.Aliases)
                .SingleAsync(static value => value.Name == "Public");
            custom.Aliases.Clear();
            custom.Aliases.Add(
                new CustomCommandAlias
                {
                    HostId = fixture.HostId,
                    Alias = "join",
                    SortOrder = 0,
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var catalog = new ViewerCommandCatalogService(
            dbFactory,
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Live("stream")),
            new RecordingCueAdmissions(),
            new UnavailableCustomCommandAutomationRuntime()
        );
        var snapshot = await catalog.LoadForHostAsync(fixture.HostId, CancellationToken.None);

        snapshot
            .Entries.Single(entry =>
                entry.Source == ViewerCommandCatalogSource.Custom && entry.Name == "!join"
            )
            .Availability.ShouldBe(ViewerCommandCatalogAvailability.Shadowed);
        snapshot.Conflicts.ShouldContain(static message => message.Contains("!join"));
    }

    [Test]
    public async Task ConfiguredAlias_DispatchingPublicChat_ReturnsSharedCatalogSnapshot()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        var liveness = new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline());
        var services = new ServiceCollection();
        _ = services.AddSingleton<IDbContextFactory<BlokeBot.Persistence.BlokeBotDbContext>>(
            dbFactory
        );
        _ = services.AddSingleton<IHostStreamLivenessProvider>(liveness);
        _ = services.AddSingleton<IOverlayCueAdmissionService>(new RecordingCueAdmissions());
        _ = services.AddSingleton<
            ICustomCommandAutomationRuntime,
            UnavailableCustomCommandAutomationRuntime
        >();
        _ = services.AddSingleton<ViewerCommandCatalogService>();
        _ = services.AddChatCommands().AddCommandModule<ViewerCommandCatalogModule>();
        await using var provider = services.BuildServiceProvider();
        var expected = await provider
            .GetRequiredService<ViewerCommandCatalogService>()
            .LoadForViewerAsync(
                "streamer",
                Message("viewer", string.Empty),
                CancellationToken.None
            );
        var responses = new List<CommandResponse>();

        await provider
            .GetRequiredService<ChatCommandDispatcher>()
            .DispatchResponsesAsync(
                new ChatMessage(
                    "viewer",
                    "streamer",
                    "!commands",
                    "raw",
                    new Dictionary<string, string>()
                ),
                (response, _) =>
                {
                    responses.Add(response);
                    return ValueTask.CompletedTask;
                },
                CancellationToken.None
            );

        responses
            .Single()
            .Message.ShouldBe($"Available viewer commands: {string.Join(", ", expected.Names)}.");
    }

    [Test]
    public async Task LongCatalog_DispatchingPublicChat_PersistsEveryOrderedPartWithoutTruncation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.CustomCommands.AddRange(
                Enumerable
                    .Range(0, 40)
                    .Select(index => new CustomCommand
                    {
                        HostId = fixture.HostId,
                        Name = $"Catalog {index:D2}",
                        Enabled = true,
                        Aliases =
                        [
                            new CustomCommandAlias
                            {
                                HostId = fixture.HostId,
                                Alias = $"catalog-command-{index:D2}",
                                SortOrder = 0,
                            },
                        ],
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow,
                    })
            );
            _ = await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        _ = services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(dbFactory);
        _ = services.AddSingleton<IHostStreamLivenessProvider>(
            new StaticLivenessProvider(new HostStreamLivenessOutcome.Offline())
        );
        _ = services.AddSingleton<IOverlayCueAdmissionService>(new RecordingCueAdmissions());
        _ = services.AddSingleton<
            ICustomCommandAutomationRuntime,
            UnavailableCustomCommandAutomationRuntime
        >();
        _ = services.AddSingleton<ViewerCommandCatalogService>();
        _ = services.AddChatCommands().AddCommandModule<ViewerCommandCatalogModule>();
        await using var provider = services.BuildServiceProvider();
        var snapshot = await provider
            .GetRequiredService<ViewerCommandCatalogService>()
            .LoadForViewerAsync(
                "streamer",
                Message("viewer", string.Empty),
                CancellationToken.None
            );
        var expected = $"Available viewer commands: {string.Join(", ", snapshot.Names)}.";
        expected.Length.ShouldBeGreaterThan(500);

        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        var queue = CreateQueue(
            outbox,
            new RecordingPublicChatTransport(),
            new ManualTestTimeProvider(Utc(12, 0, 0)),
            new BotOptions { MaxChatMessageLength = 100 }
        );
        var sender = new PublicChatCommandResponseSender(
            new PublicChatMessageSender(queue),
            NullLogger<PublicChatCommandResponseSender>.Instance
        );
        var source = new ChatMessage(
            "viewer",
            "streamer",
            "!commands",
            "raw",
            new Dictionary<string, string>()
        );

        await provider
            .GetRequiredService<ChatCommandDispatcher>()
            .DispatchResponsesAsync(
                source,
                (response, ct) => sender.SendAsync(source, response, ct),
                CancellationToken.None
            );

        await using var verify = await dbFactory.CreateDbContextAsync();
        var parts = await verify
            .PublicChatOutboxMessages.AsNoTracking()
            .OrderBy(value => value.Id)
            .Select(value => value.Message!)
            .ToArrayAsync();
        parts.Length.ShouldBeGreaterThan(1);
        parts.ShouldAllBe(part => part.Length <= 100);
        string.Join(" ", parts).ShouldBe(expected);
    }

    [Test]
    public async Task AuthorityCollisionAndBlank_SavingConfiguration_PreserveOwnedState()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedCatalogFixtureAsync(dbFactory);
        var service = new CommandsConfigurationService(
            dbFactory,
            TestEventBus.Create<AppEventKind>()
        );

        var unauthorized = await service.SaveAsync(
            Session(fixture.HostId, AuthRole.Moderator),
            fixture.HostId,
            new CommandsConfigurationSaveCommand("catalog"),
            CancellationToken.None
        );
        var staleHost = await service.SaveAsync(
            Session(fixture.HostId, AuthRole.Streamer),
            fixture.HostId + 1,
            new CommandsConfigurationSaveCommand("catalog"),
            CancellationToken.None
        );
        var collision = await service.SaveAsync(
            Session(fixture.HostId, AuthRole.Streamer),
            fixture.HostId,
            new CommandsConfigurationSaveCommand("join"),
            CancellationToken.None
        );
        var disabled = await service.SaveAsync(
            Session(fixture.HostId, AuthRole.Streamer),
            fixture.HostId,
            new CommandsConfigurationSaveCommand(" "),
            CancellationToken.None
        );

        _ = unauthorized.ShouldBeOfType<CommandsConfigurationSaveOutcome.Unauthorized>();
        _ = staleHost.ShouldBeOfType<CommandsConfigurationSaveOutcome.Unauthorized>();
        collision
            .ShouldBeOfType<CommandsConfigurationSaveOutcome.AliasConflict>()
            .Alias.ShouldBe("join");
        _ = disabled.ShouldBeOfType<CommandsConfigurationSaveOutcome.Saved>();
        (await service.LoadAsync(fixture.HostId, CancellationToken.None)).ShouldBe(
            new CommandsConfiguration(string.Empty, null)
        );
    }

    private static async Task<CatalogFixture> SeedCatalogFixtureAsync(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CommandsAliasesConfigured = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var profile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
        };
        _ = db.Profiles.Add(profile);
        _ = await db.SaveChangesAsync();
        db.CommandAliases.AddRange(
            AppAlias(host.Id, AppCommandKind.Commands, "commands"),
            AppAlias(host.Id, AppCommandKind.Points, "loyalty"),
            AppAlias(host.Id, AppCommandKind.GivePoints, "give"),
            AppAlias(host.Id, AppCommandKind.Gamble, "wager"),
            AppAlias(host.Id, AppCommandKind.Join, "enter"),
            AppAlias(host.Id, AppCommandKind.AddPoints, "secretadd"),
            AppAlias(host.Id, profile.Id, AppCommandKind.Guess, "predict"),
            AppAlias(host.Id, profile.Id, AppCommandKind.Guesses, "choices"),
            AppAlias(host.Id, profile.Id, AppCommandKind.Start, "startpredict")
        );
        _ = db.Rounds.Add(
            new GuessRound
            {
                HostId = host.Id,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Open,
                StartedAtUtc = DateTime.UtcNow,
            }
        );
        _ = db.PointsGiveaways.Add(
            new PointsGiveaway
            {
                HostId = host.Id,
                Status = PointsGiveawayStatus.Active,
                StartedAtUtc = DateTime.UtcNow,
                EndsAtUtc = DateTime.UtcNow.AddMinutes(10),
            }
        );
        _ = db.RequestBoards.Add(
            new RequestBoard
            {
                HostId = host.Id,
                Slug = "games",
                Title = "Games",
                IsOpen = true,
                VotingEnabled = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = db.PlayQueues.Add(
            new PlayQueue
            {
                HostId = host.Id,
                Slug = "main",
                Name = "Main",
                ActivityName = "Game",
                IsOpen = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        db.CustomCommands.AddRange(
            new CustomCommand
            {
                HostId = host.Id,
                Name = "Public",
                Enabled = true,
                Aliases =
                [
                    new CustomCommandAlias
                    {
                        HostId = host.Id,
                        Alias = "zeta",
                        SortOrder = 0,
                    },
                    new CustomCommandAlias
                    {
                        HostId = host.Id,
                        Alias = "alpha",
                        SortOrder = 1,
                    },
                ],
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            },
            new CustomCommand
            {
                HostId = host.Id,
                Name = "Moderator",
                Enabled = true,
                AllowEveryone = false,
                AllowModerators = true,
                Aliases =
                [
                    new CustomCommandAlias
                    {
                        HostId = host.Id,
                        Alias = "secret",
                        SortOrder = 0,
                    },
                ],
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
        return new(host.Id);
    }

    private static CommandAlias AppAlias(int hostId, AppCommandKind kind, string alias) =>
        AppAlias(hostId, null, kind, alias);

    private static async Task SetFeaturesAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        HostFeatureFlags features
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures = features;
        _ = await db.SaveChangesAsync();
    }

    private static CommandAlias AppAlias(
        int hostId,
        int? profileId,
        AppCommandKind kind,
        string alias
    ) =>
        new()
        {
            HostId = hostId,
            GuessRoundProfileId = profileId,
            Kind = kind,
            Alias = alias,
        };

    private static AuthenticatedSession Session(int hostId, AuthRole role)
    {
        var host = new BotHostChoice(hostId, "streamer", "Streamer", role);
        return new AuthenticatedSession
        {
            IsAuthenticated = true,
            UserId = role == AuthRole.Streamer ? "streamer-id" : "moderator-id",
            Login = role == AuthRole.Streamer ? "streamer" : "moderator",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private static ChatMessage Message(string login, string twitchUserId, bool moderator = false)
    {
        var tags = new Dictionary<string, string>();
        if (twitchUserId.Length > 0)
        {
            tags["user-id"] = twitchUserId;
        }
        if (moderator)
        {
            tags["mod"] = "1";
        }
        return new(login, "streamer", "!commands", "raw", tags);
    }

    private sealed class StaticLivenessProvider(HostStreamLivenessOutcome outcome)
        : IHostStreamLivenessProvider
    {
        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
            IO<HostStreamLivenessOutcome, Never>.Create(_ =>
                ValueTask.FromResult(Result<HostStreamLivenessOutcome, Never>.Success(outcome))
            );
    }

    private sealed class RecordingCueAdmissions : IOverlayCueAdmissionService
    {
        public OverlayCueReferenceOutcome Outcome { get; set; } =
            new OverlayCueReferenceOutcome.Available();

        public List<OverlayCueReferenceRequest> Requests { get; } = [];

        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(Outcome);
        }

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(new OverlayCueAdmissionCatalog([], []));

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult<OverlayCueAdmissionOutcome>(new OverlayCueAdmissionOutcome.Missing());
    }

    private sealed class CatalogAutomationRuntime(IReadOnlySet<int> availableCommandIds)
        : ICustomCommandAutomationRuntime
    {
        public IReadOnlySet<int> AvailableCommandIds { get; set; } = availableCommandIds;

        public Task<CustomCommandAutomationDispatchOutcome> DispatchAsync(
            CustomCommandAutomationDispatchRequest request,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<IReadOnlySet<int>> AvailableCommandIdsAsync(
            AutomationHostId hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(AvailableCommandIds);
    }

    private sealed record CatalogFixture(int HostId);
}
