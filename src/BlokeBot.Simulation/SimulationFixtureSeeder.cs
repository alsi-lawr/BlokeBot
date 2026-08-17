using BlokeBot.Announcements;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.Competitions;
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
        await SeedBlokeRaidAsync(db, hostId, now, cancellationToken);
        await SeedViewerPassportsAsync(db, hostId, now, cancellationToken);
        await SeedBingoAsync(db, hostId, now, cancellationToken);
        await SeedCompetitionAsync(db, hostId, now, cancellationToken);
        await SeedRaidCollaborationAsync(db, hostId, now, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);
        await SeedCollectivesAsync(db, hostId, now, cancellationToken);
        await SeedCustomCommandsAsync(db, hostId, now, cancellationToken);
        await SeedRequestBoardAsync(db, hostId, now, cancellationToken);
        await SeedPlayQueueAsync(db, hostId, now, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);
        await SeedMomentAsync(db, hostId, now, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);
        await SeedMomentAttachmentsAsync(db, hostId, now, cancellationToken);
        await SeedAlertsAsync(db, hostId, now, cancellationToken);
        await SeedAutomaticRaidShoutoutsAsync(db, hostId, now, cancellationToken);
        await SeedOverlayAsync(db, hostId, now, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);
        await SeedAutomationsAsync(db, hostId, now, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);

        return new BotHostChoice(
            hostId,
            FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.Login,
            FakeTwitch.FakeTwitchScenarioDefinition.ReadyDashboard.AuthorizedUser.DisplayName,
            AuthRole.Streamer
        );
    }

    private static async Task SeedAutomationsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (await db.AutomationFlows.AnyAsync(value => value.HostId == hostId, cancellationToken))
        {
            return;
        }

        var flowId = Guid.Parse("1b10be82-0000-4000-8000-000000000001");
        var nodeIds = Enumerable
            .Range(111, 16)
            .Select(value => Guid.Parse($"1b10be82-0000-4000-8000-{value:000000000000}"))
            .ToArray();
        var target = await db
            .OverlayInstances.Where(value =>
                value.HostId == hostId && value.Type == OverlayType.CuePlayer
            )
            .OrderBy(static value => value.Id)
            .FirstAsync(cancellationToken);
        var cue = await db
            .OverlayCues.Where(value => value.HostId == hostId)
            .OrderBy(static value => value.Id)
            .FirstAsync(cancellationToken);
        const string TransformConfiguration = """
            {
              "inputs": [
                { "port-id": "actor", "cel-identifier": "actor", "display-name": "Actor", "binding-field-id": "actor-binding", "type": "Actor", "nullability": "NonNullable", "fixed": { "login": "", "display-name": "" } },
                { "port-id": "number", "cel-identifier": "number", "display-name": "Number", "binding-field-id": "number-binding", "type": "Number", "nullability": "NonNullable", "fixed": 0 },
                { "port-id": "threshold", "cel-identifier": "threshold", "display-name": "Threshold", "binding-field-id": "threshold-binding", "type": "Boolean", "nullability": "NonNullable", "fixed": false },
                { "port-id": "arguments", "cel-identifier": "arguments_input", "display-name": "Arguments", "binding-field-id": "arguments-binding", "type": "Arguments", "nullability": "NonNullable", "fixed": [] }
              ],
              "outputs": [
                { "port-id": "message", "display-name": "Message", "type": "Text", "nullability": "NonNullable", "cel": "${actor.display_name} rolled ${format_number(number)}" },
                { "port-id": "is-high", "display-name": "Is high", "type": "Boolean", "nullability": "NonNullable", "cel": "number >= 75" },
                { "port-id": "rolled", "display-name": "Rolled", "type": "Number", "nullability": "Nullable", "cel": "number" }
              ]
            }
            """;
        const string TransformBindings = """
            {
              "actor-binding": { "mode": "Connected", "expression": null },
              "number-binding": { "mode": "Connected", "expression": null },
              "threshold-binding": { "mode": "Connected", "expression": null },
              "arguments-binding": { "mode": "Expression", "expression": { "languageVersion": 1, "source": "arguments" } }
            }
            """;
        const string ConnectedMessage =
            "{\"message\":{\"mode\":\"Connected\",\"expression\":null}}";
        const string ConnectedPredicate =
            "{\"predicate\":{\"mode\":\"Connected\",\"expression\":null}}";
        var flow = new AutomationFlow
        {
            Id = flowId,
            HostId = hostId,
            Name = "Welcome a qualifying raid",
            SchemaVersion = 1,
            IsEnabled = false,
            UseVerticalLayout = true,
            UseSmoothEdges = true,
            CreatedAtUtc = now.AddDays(-4),
            UpdatedAtUtc = now.AddMinutes(-1),
            Nodes =
            [
                AutomationNode(
                    nodeIds[0],
                    flowId,
                    "incoming-raid",
                    """{"minimum-viewers":20}""",
                    24,
                    120,
                    displayAlias: "Incoming raid"
                ),
                AutomationNode(
                    nodeIds[1],
                    flowId,
                    "random-number",
                    """{"minimum":0,"maximum":100}""",
                    48,
                    336,
                    displayAlias: "Roll"
                ),
                AutomationNode(
                    nodeIds[2],
                    flowId,
                    "random-number",
                    """{"minimum":50,"maximum":90}""",
                    48,
                    528,
                    displayAlias: "Threshold seed"
                ),
                AutomationNode(
                    nodeIds[3],
                    flowId,
                    "cel-transform",
                    TransformConfiguration,
                    192,
                    264,
                    TransformBindings,
                    "Raid announcement"
                ),
                AutomationNode(
                    nodeIds[4],
                    flowId,
                    "condition",
                    """{"predicate":false}""",
                    360,
                    96,
                    ConnectedPredicate,
                    "High roll?"
                ),
                AutomationNode(
                    nodeIds[5],
                    flowId,
                    "send-chat",
                    """{"message":"Saved message"}""",
                    360,
                    264,
                    ConnectedMessage,
                    "Send message"
                ),
                AutomationNode(
                    nodeIds[6],
                    flowId,
                    "send-chat",
                    """{"message":"Saved log message"}""",
                    360,
                    432,
                    ConnectedMessage,
                    "Log result"
                ),
                AutomationNode(
                    nodeIds[7],
                    flowId,
                    "send-chat",
                    """{"message":"Saved announcement"}""",
                    360,
                    600,
                    ConnectedMessage,
                    "Announce result"
                ),
                AutomationNode(
                    nodeIds[8],
                    flowId,
                    "play-overlay-cue",
                    $$"""{"target-id":"{{target.PublicId:D}}","cue-id":"{{cue.PublicId:D}}"}""",
                    528,
                    24,
                    displayAlias: "Play celebration"
                ),
                AutomationNode(
                    nodeIds[9],
                    flowId,
                    "delay",
                    """{"duration-milliseconds":2000}""",
                    528,
                    168,
                    displayAlias: "Wait 2 seconds"
                ),
                AutomationNode(
                    nodeIds[10],
                    flowId,
                    "play-overlay-cue",
                    $$"""{"target-id":"{{target.PublicId:D}}","cue-id":"{{cue.PublicId:D}}"}""",
                    528,
                    312,
                    displayAlias: "Show roll"
                ),
                AutomationNode(
                    nodeIds[11],
                    flowId,
                    "merge-branches",
                    "{}",
                    528,
                    456,
                    displayAlias: "Merge branches"
                ),
                AutomationNode(
                    nodeIds[12],
                    flowId,
                    "send-chat",
                    """{"message":"Audit complete"}""",
                    696,
                    552,
                    displayAlias: "Save audit"
                ),
                AutomationNode(
                    nodeIds[13],
                    flowId,
                    "condition",
                    """{"predicate":false}""",
                    528,
                    600,
                    ConnectedPredicate,
                    "Check result"
                ),
                AutomationNode(
                    nodeIds[14],
                    flowId,
                    "stream-online",
                    "{}",
                    192,
                    24,
                    displayAlias: "Stream online"
                ),
                AutomationNode(
                    nodeIds[15],
                    flowId,
                    "send-chat",
                    """{"message":"Stream started"}""",
                    360,
                    744,
                    displayAlias: "Stream notice"
                ),
            ],
            Edges =
            [
                FlowEdge(31, flowId, nodeIds[0], "flow", nodeIds[4]),
                FlowEdge(32, flowId, nodeIds[0], "flow", nodeIds[5]),
                FlowEdge(33, flowId, nodeIds[4], "yes", nodeIds[8]),
                FlowEdge(34, flowId, nodeIds[4], "no", nodeIds[6]),
                FlowEdge(35, flowId, nodeIds[8], "complete", nodeIds[9]),
                FlowEdge(36, flowId, nodeIds[9], "complete", nodeIds[7]),
                FlowEdge(37, flowId, nodeIds[5], "complete", nodeIds[7]),
                FlowEdge(38, flowId, nodeIds[6], "complete", nodeIds[7]),
                FlowEdge(39, flowId, nodeIds[7], "complete", nodeIds[13]),
                FlowEdge(40, flowId, nodeIds[13], "yes", nodeIds[10]),
                FlowEdge(41, flowId, nodeIds[13], "no", nodeIds[11]),
                FlowEdge(42, flowId, nodeIds[10], "complete", nodeIds[12]),
                FlowEdge(43, flowId, nodeIds[11], "complete", nodeIds[12]),
                FlowEdge(44, flowId, nodeIds[14], "flow", nodeIds[15]),
                FlowEdge(45, flowId, nodeIds[15], "complete", nodeIds[12]),
                FlowEdge(46, flowId, nodeIds[0], "flow", nodeIds[12]),
                DataEdge(47, flowId, nodeIds[0], "actor", nodeIds[3], "actor"),
                DataEdge(48, flowId, nodeIds[1], "number", nodeIds[3], "number"),
                DataEdge(49, flowId, nodeIds[2], "number", nodeIds[3], "threshold"),
                DataEdge(50, flowId, nodeIds[3], "message", nodeIds[5], "message"),
                DataEdge(51, flowId, nodeIds[3], "message", nodeIds[6], "message"),
                DataEdge(52, flowId, nodeIds[3], "message", nodeIds[7], "message"),
                DataEdge(53, flowId, nodeIds[3], "is-high", nodeIds[4], "predicate"),
                DataEdge(54, flowId, nodeIds[3], "is-high", nodeIds[13], "predicate"),
            ],
        };
        _ = db.AutomationFlows.Add(flow);
        db.AutomationFlows.AddRange(
            SampleFlow(
                hostId,
                "Celebrate a new follow",
                "stream-online",
                now.AddMinutes(-10),
                Guid.Parse("1b10be82-0000-4000-8000-000000000002"),
                enabled: false
            ),
            SampleFlow(
                hostId,
                "Thank large cheers",
                "cheer",
                now.AddMinutes(-20),
                Guid.Parse("1b10be82-0000-4000-8000-000000000003"),
                """{"minimum-bits":500}"""
            ),
            SampleFlow(
                hostId,
                "Close predictions",
                "prediction-ended",
                now.AddMinutes(-30),
                Guid.Parse("1b10be82-0000-4000-8000-000000000004")
            )
        );
        _ = db.AutomationFlowRuns.Add(
            new AutomationFlowRun
            {
                Id = Guid.Parse("1b10be82-0000-4000-8000-000000000021"),
                FlowId = flowId,
                HostId = hostId,
                AutomationGeneration = 0,
                RequiredFeatures = HostFeatureFlags.Automations,
                ContextSchemaVersion = 1,
                SourceDefinitionId = "incoming-raid",
                SourceNodeId = nodeIds[0],
                SourceOccurrenceId = Guid.Parse("1b10be82-0000-4000-8000-000000000022"),
                ContextJson = "{}",
                DefinitionJson = AutomationRuntimeSerialization.SerializeDefinition(flow),
                Status = AutomationFlowRunStatus.Completed,
                StartedAtUtc = now.AddMinutes(-12),
                CompletedAtUtc = now.AddMinutes(-12).AddSeconds(1),
                NodeRuns =
                [
                    AutomationNodeRun(nodeIds[0], 0, "source-received", now),
                    AutomationNodeRun(nodeIds[3], 1, "transform-completed", now),
                    AutomationNodeRun(nodeIds[5], 2, "action-succeeded", now),
                ],
            }
        );
    }

    private static AutomationFlow SampleFlow(
        int hostId,
        string name,
        string sourceDefinition,
        DateTime updatedAtUtc,
        Guid id,
        string configuration = "{}",
        bool enabled = true
    )
    {
        var nodeBytes = id.ToByteArray();
        nodeBytes[15] = (byte)(nodeBytes[15] + 32);
        var nodeId = new Guid(nodeBytes);
        return new AutomationFlow
        {
            Id = id,
            HostId = hostId,
            Name = name,
            SchemaVersion = 1,
            IsEnabled = enabled,
            CreatedAtUtc = updatedAtUtc.AddDays(-2),
            UpdatedAtUtc = updatedAtUtc,
            Nodes = [AutomationNode(nodeId, id, sourceDefinition, configuration, 72, 168)],
        };
    }

    private static AutomationFlowNode AutomationNode(
        Guid id,
        Guid flowId,
        string definitionId,
        string configuration,
        int canvasX,
        int canvasY,
        string inputBindings = "{}",
        string? displayAlias = null
    ) =>
        new()
        {
            Id = id,
            FlowId = flowId,
            DefinitionId = definitionId,
            DefinitionSchemaVersion = 1,
            ConfigurationJson = configuration,
            InputBindingsJson = inputBindings,
            ExpressionLanguageVersion = 1,
            CanvasX = canvasX,
            CanvasY = canvasY,
            DisplayAlias = displayAlias,
        };

    private static AutomationFlowEdge FlowEdge(
        int id,
        Guid flowId,
        Guid sourceNodeId,
        string sourcePortId,
        Guid targetNodeId
    ) =>
        new()
        {
            Id = Guid.Parse($"1b10be82-0000-4000-8000-{id:000000000000}"),
            FlowId = flowId,
            Kind = PersistedAutomationEdgeKind.Flow,
            SourceNodeId = sourceNodeId,
            SourcePortId = sourcePortId,
            TargetNodeId = targetNodeId,
            TargetPortId = "flow",
        };

    private static AutomationFlowEdge DataEdge(
        int id,
        Guid flowId,
        Guid sourceNodeId,
        string sourcePortId,
        Guid targetNodeId,
        string targetPortId
    ) =>
        new()
        {
            Id = Guid.Parse($"1b10be82-0000-4000-8000-{id:000000000000}"),
            FlowId = flowId,
            Kind = PersistedAutomationEdgeKind.Data,
            SourceNodeId = sourceNodeId,
            SourcePortId = sourcePortId,
            TargetNodeId = targetNodeId,
            TargetPortId = targetPortId,
        };

    private static AutomationNodeRun AutomationNodeRun(
        Guid nodeId,
        long sequence,
        string outcomeCode,
        DateTime now
    ) =>
        new()
        {
            NodeId = nodeId,
            Sequence = sequence,
            Status = AutomationNodeRunStatus.Succeeded,
            AvailableAtUtc = now,
            StartedAtUtc = now,
            CompletedAtUtc = now,
            OutcomeCode = outcomeCode,
        };

    private static async Task SeedBlokeRaidAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (
            await db.BlokeRaidCampaigns.AnyAsync(value => value.HostId == hostId, cancellationToken)
        )
        {
            return;
        }

        _ = db.BlokeRaidConfigurations.Add(
            new BlokeRaidConfiguration
            {
                HostId = hostId,
                Revision = 3,
                ResetPolicy = BlokeRaidResetPolicy.Weekly,
                WeeklyResetDay = (int)DayOfWeek.Monday,
                WeeklyResetHourUtc = 9,
                NextWeeklyResetAtUtc = now.AddDays(5).Date.AddHours(9),
                UpdatedAtUtc = now.AddMinutes(-8),
            }
        );
        var campaign = new BlokeRaidCampaign
        {
            HostId = hostId,
            PublicId = Guid.Parse("d9ca8015-3c1d-460c-9efb-251b94a077aa"),
            StartOperationKey = "simulation-active-raid",
            Status = BlokeRaidCampaignStatus.Active,
            BossName = "The Null Wyrm",
            MaximumHealth = 25_000,
            CurrentHealth = 12_480,
            MaximumWard = 1_000,
            CurrentWard = 840,
            CurrentPhase = 2,
            VictoryPointReward = "250",
            ResetPolicy = BlokeRaidResetPolicy.Weekly,
            StartedAtUtc = now.AddDays(-6).AddHours(-2),
            EndsAtUtc = now.AddDays(1).AddHours(22),
            Revision = 326,
        };
        campaign.Contributions.AddRange(
            new BlokeRaidContribution
            {
                HostId = hostId,
                ViewerTwitchUserId = "raid-mossybyte",
                ViewerLogin = "mossybyte",
                ViewerDisplayName = "MossyByte",
                Damage = 2_090,
                WardRestored = 328,
                ActionCount = 58,
                SpecialCount = 18,
                CorrectGuessCount = 6,
                LastContributedAtUtc = now.AddSeconds(-12),
            },
            new BlokeRaidContribution
            {
                HostId = hostId,
                ViewerTwitchUserId = "raid-pixelknight",
                ViewerLogin = "pixelknight",
                ViewerDisplayName = "PixelKnight",
                Damage = 1_845,
                WardRestored = 186,
                ActionCount = 47,
                SpecialCount = 12,
                CorrectGuessCount = 4,
                LastContributedAtUtc = now.AddSeconds(-31),
            },
            new BlokeRaidContribution
            {
                HostId = hostId,
                ViewerTwitchUserId = "raid-teacupmage",
                ViewerLogin = "teacupmage",
                ViewerDisplayName = "TeacupMage",
                Damage = 1_402,
                WardRestored = 474,
                ActionCount = 45,
                SpecialCount = 9,
                CorrectGuessCount = 8,
                LastContributedAtUtc = now.AddSeconds(-44),
            },
            new BlokeRaidContribution
            {
                HostId = hostId,
                ViewerTwitchUserId = "raid-orbitalowl",
                ViewerLogin = "orbitalowl",
                ViewerDisplayName = "OrbitalOwl",
                Damage = 1_311,
                WardRestored = 221,
                ActionCount = 36,
                SpecialCount = 7,
                CorrectGuessCount = 3,
                LastContributedAtUtc = now.AddMinutes(-3),
            }
        );
        campaign.Actions.AddRange(
            RaidAction(
                hostId,
                "simulation-nova",
                BlokeRaidActionKind.Special,
                "raid-mossybyte",
                "mossybyte",
                "MossyByte",
                12,
                12_492,
                12_480,
                840,
                840,
                "75",
                now.AddSeconds(-12)
            ),
            RaidAction(
                hostId,
                "simulation-attack",
                BlokeRaidActionKind.Attack,
                "raid-pixelknight",
                "pixelknight",
                "PixelKnight",
                5,
                12_497,
                12_492,
                833,
                833,
                "0",
                now.AddSeconds(-31)
            ),
            RaidAction(
                hostId,
                "simulation-mend",
                BlokeRaidActionKind.Mend,
                "raid-teacupmage",
                "teacupmage",
                "TeacupMage",
                7,
                12_497,
                12_497,
                833,
                840,
                "0",
                now.AddSeconds(-44)
            ),
            new BlokeRaidAction
            {
                HostId = hostId,
                OperationKey = "guess:184",
                Kind = BlokeRaidActionKind.CorrectGuess,
                Source = BlokeRaidActionSource.Guessing,
                StreamKey = "guessing:184",
                Outcome = 24,
                PointCost = "0",
                BossHealthBefore = 12_521,
                BossHealthAfter = 12_497,
                WardBefore = 833,
                WardAfter = 833,
                PhaseAfter = 2,
                GuessRoundId = 184,
                Response = "Correct guessers dealt 24 damage to the boss.",
                OccurredAtUtc = now.AddMinutes(-3),
            }
        );
        campaign.Events.Add(
            new BlokeRaidDomainEvent
            {
                HostId = hostId,
                Kind = BlokeRaidEventKind.PhaseChanged,
                OperationKey = $"phase:{campaign.PublicId:N}:2",
                PublicPayload =
                    "{\"phase\":2,\"health\":16250,\"response\":\"Its armour fractures. The raid drives into the exposed scales.\"}",
                OccurredAtUtc = now.AddDays(-2),
            }
        );
        var recap = new BlokeRaidCampaign
        {
            HostId = hostId,
            PublicId = Guid.Parse("84734c96-ef5e-4f19-8a83-a81159068c13"),
            StartOperationKey = "simulation-completed-raid",
            Status = BlokeRaidCampaignStatus.Victory,
            BossName = "The Static Colossus",
            MaximumHealth = 18_000,
            CurrentHealth = 0,
            MaximumWard = 750,
            CurrentWard = 612,
            CurrentPhase = 3,
            VictoryPointReward = "200",
            ResetPolicy = BlokeRaidResetPolicy.Manual,
            StartedAtUtc = now.AddDays(-16),
            EndsAtUtc = now.AddDays(-9),
            CompletedAtUtc = now.AddDays(-10).AddHours(-4),
            VictoryRewardedAtUtc = now.AddDays(-10).AddHours(-4),
            Revision = 441,
            Contributions =
            [
                new BlokeRaidContribution
                {
                    HostId = hostId,
                    ViewerTwitchUserId = "raid-mossybyte",
                    ViewerLogin = "mossybyte",
                    ViewerDisplayName = "MossyByte",
                    Damage = 3_418,
                    WardRestored = 410,
                    ActionCount = 81,
                    SpecialCount = 21,
                    CorrectGuessCount = 7,
                    LastContributedAtUtc = now.AddDays(-10).AddHours(-4),
                },
            ],
        };
        db.BlokeRaidCampaigns.AddRange(campaign, recap);
    }

    private static BlokeRaidAction RaidAction(
        int hostId,
        string operationKey,
        BlokeRaidActionKind kind,
        string userId,
        string login,
        string displayName,
        int outcome,
        int healthBefore,
        int healthAfter,
        int wardBefore,
        int wardAfter,
        string pointCost,
        DateTime occurredAtUtc
    ) =>
        new()
        {
            HostId = hostId,
            OperationKey = operationKey,
            Kind = kind,
            Source = BlokeRaidActionSource.Chat,
            ViewerTwitchUserId = userId,
            ViewerLogin = login,
            ViewerDisplayName = displayName,
            StreamKey = "simulation-stream",
            Outcome = outcome,
            PointCost = pointCost,
            BossHealthBefore = healthBefore,
            BossHealthAfter = healthAfter,
            WardBefore = wardBefore,
            WardAfter = wardAfter,
            PhaseAfter = 2,
            Response = kind switch
            {
                BlokeRaidActionKind.Attack => $"Attack dealt {outcome} damage.",
                BlokeRaidActionKind.Mend => $"The raid ward recovered {outcome}.",
                _ => $"The point-funded special dealt {outcome} damage.",
            },
            OccurredAtUtc = occurredAtUtc,
        };

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
            var startedAtUtc = now.AddDays(-offset).AddHours(-4);
            var session = new ViewerPassportStreamSession
            {
                HostId = hostId,
                TwitchStreamId = $"simulation-stream-{6 - offset}",
                StartedAtUtc = startedAtUtc,
                ContinuityGeneration = 0,
                RecordedAtUtc = startedAtUtc.AddHours(1),
            };
            _ = db.ViewerPassportStreamSessions.Add(session);
            db.ViewerPassportStreamAttendances.AddRange(
                new ViewerPassportStreamAttendance
                {
                    HostId = hostId,
                    PassportId = streamer.Id,
                    StreamSession = session,
                    ContinuityGeneration = 0,
                    FirstSeenAtUtc = now.AddDays(-offset).AddHours(-2),
                },
                new ViewerPassportStreamAttendance
                {
                    HostId = hostId,
                    PassportId = nightOwl.Id,
                    StreamSession = session,
                    ContinuityGeneration = 0,
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
            Name = "Perfect comeback",
            Description = "Win after falling behind during the summer climb.",
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
        db.MomentCandidates.AddRange(
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
            },
            new MomentCandidate
            {
                PublicId = Guid.Parse("a2d6bb24-208b-46e4-a6f4-09fa3b307356"),
                HostId = hostId,
                StreamIdentity = "stream-2057",
                State = MomentCandidateState.Approved,
                PublicTitle = "Zero-health comeback",
                PublicCategory = "Challenge run",
                CapturedAtUtc = now.AddDays(-8).AddMinutes(-31),
                LastCapturedAtUtc = now.AddDays(-8).AddMinutes(-31),
                ApprovedAtUtc = now.AddDays(-8).AddMinutes(-25),
            },
            new MomentCandidate
            {
                PublicId = Guid.Parse("b534da56-eb18-45d2-94cf-c5b787728b55"),
                HostId = hostId,
                StreamIdentity = "stream-2098",
                State = MomentCandidateState.Approved,
                PublicTitle = "Bracket reset denied",
                PublicCategory = "Tournament",
                CapturedAtUtc = now.AddDays(-1).AddMinutes(-27),
                LastCapturedAtUtc = now.AddDays(-1).AddMinutes(-27),
                ApprovedAtUtc = now.AddDays(-1).AddMinutes(-20),
            }
        );
    }

    private static async Task SeedMomentAttachmentsAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (await db.MomentAttachments.AnyAsync(value => value.HostId == hostId, cancellationToken))
        {
            return;
        }

        var bountyId = await db
            .Bounties.Where(value =>
                value.HostId == hostId
                && value.PublicId == Guid.Parse("3e25c2dc-6bc2-41fc-8808-055677f26195")
            )
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);
        var achievementId = await db
            .CommunityDefinitions.Where(value =>
                value.HostId == hostId
                && value.PublicId == Guid.Parse("6db0bd9a-9e69-49c5-a4bd-b6fa2604d30c")
            )
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);
        var moments = await db
            .MomentCandidates.Where(value => value.HostId == hostId)
            .Where(value =>
                value.PublicId == Guid.Parse("75a75ee9-cfed-47da-ad88-762f67f8c0a5")
                || value.PublicId == Guid.Parse("a2d6bb24-208b-46e4-a6f4-09fa3b307356")
            )
            .ToDictionaryAsync(value => value.PublicId, cancellationToken);

        db.MomentAttachments.AddRange(
            new MomentAttachment
            {
                HostId = hostId,
                MomentCandidateId = moments[Guid.Parse("75a75ee9-cfed-47da-ad88-762f67f8c0a5")].Id,
                BountyId = bountyId,
                AttachedAtUtc = now.AddMinutes(-9),
            },
            new MomentAttachment
            {
                HostId = hostId,
                MomentCandidateId = moments[Guid.Parse("a2d6bb24-208b-46e4-a6f4-09fa3b307356")].Id,
                CommunityDefinitionId = achievementId,
                AttachedAtUtc = now.AddMinutes(-7),
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

    private static async Task SeedCompetitionAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var publicId = Guid.Parse("f54fead3-1c88-4c65-b492-68d13bb19cad");
        if (await db.Competitions.AnyAsync(value => value.PublicId == publicId, cancellationToken))
        {
            return;
        }
        var competition = new Competition
        {
            HostId = hostId,
            PublicId = publicId,
            CreationOperationId = Guid.Parse("e65a9bf8-0751-40e1-b537-cfa0d079f3e9"),
            Name = "Summer Community Circuit",
            Description =
                "Seven rounds of friendly community matches with a final placement reward.",
            Format = CompetitionFormat.RoundRobin,
            EntryKind = CompetitionEntryKind.Individual,
            Status = CompetitionStatus.Running,
            Seeding = CompetitionSeeding.Random,
            Tiebreak = CompetitionTiebreak.ScoreDifferenceThenScoreFor,
            Capacity = 8,
            TeamSize = 1,
            MinimumPoints = "100",
            WinPoints = 3,
            DrawPoints = 1,
            LossPoints = 0,
            Seed = "summer-circuit-26",
            AlgorithmVersion = "blokebot-shuffle-v1",
            ReminderHoursBefore = 24,
            ReminderMessage =
                "Reminder: {competition} round {round} is scheduled for {scheduled}. {public_url}",
            WinnerPoints = "500",
            RunnerUpPoints = "250",
            WinnerAchievementKey = "circuit-champion",
            PrivateLobbyInformation = "Lobby details are shared with entrants by whisper.",
            Revision = 9,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddMinutes(-15),
            RegistrationOpenedAtUtc = now.AddDays(-9),
            StartedAtUtc = now.AddDays(-4),
        };
        var names = new[]
        {
            "PixelPilot",
            "NightOwl",
            "CozyCactus",
            "ByteBard",
            "MapleMage",
            "NovaNomad",
        };
        foreach (var (name, index) in names.Select((name, index) => (name, index)))
        {
            competition.Entrants.Add(
                new CompetitionEntrant
                {
                    HostId = hostId,
                    PublicId = Guid.Parse($"00000000-0000-4000-8000-{index + 1:000000000000}"),
                    RegistrationOperationId = Guid.Parse(
                        $"10000000-0000-4000-8000-{index + 1:000000000000}"
                    ),
                    Name = name,
                    RegisteredAtUtc = now.AddDays(-8).AddMinutes(index),
                    Members =
                    [
                        new()
                        {
                            HostId = hostId,
                            TwitchUserId = $"competition-viewer-{index + 1}",
                            Login = name.ToLowerInvariant(),
                            DisplayName = name,
                            PrivateContact = $"{name.ToLowerInvariant()} private contact",
                        },
                    ],
                }
            );
        }
        var scores = new[]
        {
            (0, 1, 3, 1),
            (2, 3, 2, 2),
            (4, 5, 0, 1),
            (0, 2, 2, 0),
            (1, 4, 3, 2),
            (3, 5, 1, 0),
        };
        foreach (var (fixture, index) in scores.Select((fixture, index) => (fixture, index)))
        {
            competition.Matches.Add(
                new CompetitionMatch
                {
                    HostId = hostId,
                    PublicId = Guid.Parse($"20000000-0000-4000-8000-{index + 1:000000000000}"),
                    Round = (index / 3) + 1,
                    Position = index % 3,
                    EntrantA = competition.Entrants[fixture.Item1],
                    EntrantB = competition.Entrants[fixture.Item2],
                    ScoreA = fixture.Item3,
                    ScoreB = fixture.Item4,
                    WinnerEntrant =
                        fixture.Item3 == fixture.Item4
                            ? null
                            : competition.Entrants[
                                fixture.Item3 > fixture.Item4 ? fixture.Item1 : fixture.Item2
                            ],
                    Status = CompetitionMatchStatus.Confirmed,
                    ScheduledAtUtc = now.AddDays(index - 6),
                    ConfirmedAtUtc = now.AddDays(index - 6).AddHours(2),
                }
            );
        }
        competition.Matches.Add(
            new CompetitionMatch
            {
                HostId = hostId,
                PublicId = Guid.Parse("20000000-0000-4000-8000-000000000099"),
                Round = 3,
                Position = 0,
                EntrantA = competition.Entrants[0],
                EntrantB = competition.Entrants[3],
                Status = CompetitionMatchStatus.Pending,
                ScheduledAtUtc = now.AddDays(1),
                ReminderDueAtUtc = now,
            }
        );
        competition.Audits.Add(
            new CompetitionAudit
            {
                HostId = hostId,
                OperationId = Guid.Parse("30000000-0000-4000-8000-000000000001"),
                Action = CompetitionAuditAction.Started,
                ActorTwitchUserId = "1000",
                ActorLogin = "streamer",
                PrivateReason = "Schedule approved for the summer circuit.",
                OccurredAtUtc = now.AddDays(-4),
            }
        );
        competition.MilestoneRewards.Add(
            new CompetitionMilestoneRewardRule
            {
                HostId = hostId,
                WinsRequired = 3,
                Points = "100",
                AchievementKey = string.Empty,
            }
        );
        var predictionLeague = new Competition
        {
            HostId = hostId,
            PublicId = Guid.Parse("f54fead3-1c88-4c65-b492-68d13bb19cae"),
            CreationOperationId = Guid.Parse("e65a9bf8-0751-40e1-b537-cfa0d079f3ea"),
            Name = "Friday Prediction League",
            Description = "A weekly correct-score prediction league.",
            Format = CompetitionFormat.PredictionLeague,
            EntryKind = CompetitionEntryKind.Individual,
            Status = CompetitionStatus.Registration,
            Seeding = CompetitionSeeding.Random,
            Tiebreak = CompetitionTiebreak.ScoreForThenWins,
            Capacity = 12,
            TeamSize = 1,
            WinPoints = 3,
            DrawPoints = 1,
            Seed = "friday-predictions-26",
            AlgorithmVersion = CompetitionSchedule.AlgorithmVersion,
            ReminderHoursBefore = 12,
            ReminderMessage =
                "Reminder: {competition} round {round} is scheduled for {scheduled}. {public_url}",
            Revision = 4,
            CreatedAtUtc = now.AddDays(-3),
            UpdatedAtUtc = now.AddHours(-2),
            RegistrationOpenedAtUtc = now.AddDays(-2),
        };
        foreach (var (name, index) in names.Take(3).Select((name, index) => (name, index)))
        {
            predictionLeague.Entrants.Add(
                new CompetitionEntrant
                {
                    HostId = hostId,
                    PublicId = Guid.Parse($"40000000-0000-4000-8000-{index + 1:000000000000}"),
                    RegistrationOperationId = Guid.Parse(
                        $"41000000-0000-4000-8000-{index + 1:000000000000}"
                    ),
                    Name = name,
                    RegisteredAtUtc = now.AddDays(-2).AddMinutes(index),
                    Members =
                    [
                        new()
                        {
                            HostId = hostId,
                            TwitchUserId = $"prediction-viewer-{index + 1}",
                            Login = name.ToLowerInvariant(),
                            DisplayName = name,
                        },
                    ],
                }
            );
        }
        var archived = new Competition
        {
            HostId = hostId,
            PublicId = Guid.Parse("f54fead3-1c88-4c65-b492-68d13bb19caf"),
            CreationOperationId = Guid.Parse("e65a9bf8-0751-40e1-b537-cfa0d079f3eb"),
            Name = "Spring Knockout",
            Description = "Archived single-elimination results from the spring event.",
            Format = CompetitionFormat.Tournament,
            EntryKind = CompetitionEntryKind.Individual,
            Status = CompetitionStatus.Archived,
            Seeding = CompetitionSeeding.Seeded,
            Tiebreak = CompetitionTiebreak.ScoreDifferenceThenScoreFor,
            Capacity = 8,
            TeamSize = 1,
            WinPoints = 3,
            DrawPoints = 1,
            Seed = "spring-knockout-26",
            AlgorithmVersion = CompetitionSchedule.AlgorithmVersion,
            ReminderMessage = "Reminder",
            Revision = 14,
            CreatedAtUtc = now.AddDays(-90),
            UpdatedAtUtc = now.AddDays(-60),
            CompletedAtUtc = now.AddDays(-61),
            ArchivedAtUtc = now.AddDays(-60),
        };
        db.Competitions.AddRange(competition, predictionLeague, archived);
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

    private static async Task SeedRaidCollaborationAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (
            await db.RaidCollaborationSettings.AnyAsync(
                value => value.HostId == hostId,
                cancellationToken
            )
        )
        {
            return;
        }

        _ = db.RaidCollaborationSettings.Add(
            new RaidCollaborationSettings
            {
                HostId = hostId,
                WelcomeEnabled = true,
                WelcomeMessage = "Welcome {display_name} and community! 💜",
                DeduplicationWindowMinutes = 60,
                Language = "en",
                EligibleCategories = "Celeste\nMakers & Crafting",
                RelationshipCooldownHours = 336,
                IncludeFollowedLiveChannels = true,
                UpdatedAtUtc = now,
            }
        );
        db.ApprovedRaidChannels.AddRange(
            new ApprovedRaidChannel
            {
                HostId = hostId,
                TwitchUserId = "maple-id",
                Login = "maplepixel",
                DisplayName = "MaplePixel",
                ApprovedClipId = "maple-clip",
                ApprovedAtUtc = now.AddDays(-70),
                UpdatedAtUtc = now.AddDays(-4),
            },
            new ApprovedRaidChannel
            {
                HostId = hostId,
                TwitchUserId = "cozy-id",
                Login = "cozyworkshop",
                DisplayName = "CozyWorkshop",
                ApprovedAtUtc = now.AddDays(-30),
                UpdatedAtUtc = now.AddDays(-30),
            },
            new ApprovedRaidChannel
            {
                HostId = hostId,
                Login = "offlinereviewer",
                DisplayName = "OfflineReviewer",
                ApprovedAtUtc = now.AddDays(-20),
                UpdatedAtUtc = now.AddDays(-20),
            }
        );
        db.RaidCollaborationHistory.AddRange(
            History(
                hostId,
                "simulation-raid-incoming",
                RaidDirection.Incoming,
                "maple-id",
                "maplepixel",
                "MaplePixel",
                93,
                "Celeste",
                "maple-previous-stream",
                now.AddMinutes(-8),
                RaidWelcomeOutcome.Delivered,
                RaidShoutoutOutcome.Sent
            ),
            History(
                hostId,
                "simulation-raid-outgoing",
                RaidDirection.Outgoing,
                "old-friend-id",
                "oldfriend",
                "OldFriend",
                117,
                "Hades II",
                "old-friend-stream",
                now.AddDays(-38),
                RaidWelcomeOutcome.NotConfigured,
                RaidShoutoutOutcome.NotConfigured
            )
        );
    }

    private static async Task SeedCollectivesAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var publicId = Guid.Parse("3f78b947-a0f8-4872-ae3b-a876a27e58a0");
        if (await db.Collectives.AnyAsync(value => value.PublicId == publicId, cancellationToken))
        {
            return;
        }
        var hosts = new[]
        {
            (Login: "cozyworkshop", DisplayName: "CosyWorkshop", TwitchId: "cozy-id"),
            (Login: "maplepixel", DisplayName: "MaplePixel", TwitchId: "maple-id"),
            (Login: "bytebard", DisplayName: "ByteBard", TwitchId: "byte-id"),
        };
        foreach (var seed in hosts)
        {
            if (!await db.Hosts.AnyAsync(value => value.Login == seed.Login, cancellationToken))
            {
                _ = db.Hosts.Add(
                    new BotHost
                    {
                        Login = seed.Login,
                        DisplayName = seed.DisplayName,
                        TwitchUserId = seed.TwitchId,
                        EnabledFeatures = HostFeatureFlags.All,
                        TimeZoneId = "UTC",
                        CreatedAtUtc = now.AddDays(-60),
                    }
                );
            }
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        var hostMap = await db
            .Hosts.Where(value =>
                value.Id == hostId || hosts.Select(host => host.Login).Contains(value.Login)
            )
            .ToDictionaryAsync(value => value.Login, cancellationToken);
        var sample = await db.Hosts.SingleAsync(value => value.Id == hostId, cancellationToken);
        var cosy = hostMap["cozyworkshop"];
        var maple = hostMap["maplepixel"];
        var byteHost = hostMap["bytebard"];
        var collective = new Collective
        {
            PublicId = publicId,
            CreationOperationId = Guid.Parse("9f5e198b-a204-4e03-8a55-dd0fb63669d8"),
            Name = "Cosy Circuit",
            Revision = 14,
            CreatedAtUtc = now.AddDays(-12),
            UpdatedAtUtc = now.AddMinutes(-2),
            Memberships =
            [
                Membership(
                    sample.Id,
                    CollectiveMembershipRole.Coordinator,
                    CollectiveMembershipStatus.Active,
                    now.AddDays(-12)
                ),
                Membership(
                    cosy.Id,
                    CollectiveMembershipRole.Participant,
                    CollectiveMembershipStatus.Active,
                    now.AddDays(-10)
                ),
                Membership(
                    maple.Id,
                    CollectiveMembershipRole.Participant,
                    CollectiveMembershipStatus.Active,
                    now.AddDays(-8)
                ),
                Membership(
                    byteHost.Id,
                    CollectiveMembershipRole.Participant,
                    CollectiveMembershipStatus.Pending,
                    now.AddDays(-1)
                ),
            ],
            TournamentReference = new()
            {
                OwnerHostId = sample.Id,
                CompetitionPublicId = Guid.Parse("f54fead3-1c88-4c65-b492-68d13bb19cad"),
                Name = "Summer Community Circuit",
                Format = CompetitionFormat.RoundRobin,
                Status = CompetitionStatus.Running,
                Round = 3,
                EntrantCount = 6,
                ConfirmedResultCount = 6,
                Revision = 7,
                LastSourceEventAtUtc = now.AddMinutes(-2),
                UpdatedAtUtc = now.AddMinutes(-2),
            },
            RaidRelay = new()
            {
                Name = "Weekend makers relay",
                CurrentHostId = cosy.Id,
                AggregateViewerCount = 93,
                Status = CollectiveWorkflowStatus.Pending,
                Revision = 5,
                LastSourceEventAtUtc = now.AddMinutes(-3),
                UpdatedAtUtc = now.AddMinutes(-3),
                Handoffs =
                [
                    new()
                    {
                        OperationId = "simulation-relay-arrival-7d31",
                        FromHostId = sample.Id,
                        ToHostId = cosy.Id,
                        AggregateViewerCount = 93,
                        Status = CollectiveRaidHandoffStatus.Confirmed,
                        OccurredAtUtc = now.AddMinutes(-3),
                        UpdatedAtUtc = now.AddMinutes(-3),
                    },
                ],
            },
            Goal = new()
            {
                Name = "Build 12 comfort kits",
                UnitName = "kit",
                Target = 12,
                Current = 8,
                DeadlineUtc = now.AddDays(33).AddHours(8),
                Status = CollectiveWorkflowStatus.Active,
                Revision = 9,
                UpdatedAtUtc = now.AddMinutes(-2),
                HostTotals =
                [
                    GoalTotal(sample.Id, "00000000-0000-4000-8000-000000000301", 3, now),
                    GoalTotal(cosy.Id, "00000000-0000-4000-8000-000000000302", 3, now),
                    GoalTotal(maple.Id, "00000000-0000-4000-8000-000000000303", 2, now),
                ],
            },
            Audits =
            [
                Audit(
                    "simulation-reference-7d31",
                    CollectiveAuditAction.TournamentReferenceChanged,
                    sample.Id,
                    sample.Id,
                    now.AddMinutes(-2)
                ),
                Audit(
                    "simulation-invitation-98a4",
                    CollectiveAuditAction.HostInvited,
                    sample.Id,
                    byteHost.Id,
                    now.AddDays(-1)
                ),
                Audit(
                    "simulation-created-4410",
                    CollectiveAuditAction.Created,
                    sample.Id,
                    sample.Id,
                    now.AddDays(-12)
                ),
            ],
        };
        _ = db.Collectives.Add(collective);
        _ = db.CollectiveLocalSettings.Add(
            new CollectiveLocalSetting
            {
                Collective = collective,
                HostId = sample.Id,
                Notification = CollectiveLocalNotification.Moderators,
                Revision = 2,
                UpdatedAtUtc = now.AddHours(-2),
            }
        );
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private static CollectiveMembership Membership(
        int hostId,
        CollectiveMembershipRole role,
        CollectiveMembershipStatus status,
        DateTime occurredAtUtc
    ) =>
        new()
        {
            HostId = hostId,
            Role = role,
            Status = status,
            AcceptWorkAfterUtc = occurredAtUtc,
            InvitedAtUtc = occurredAtUtc,
            RespondedAtUtc = status == CollectiveMembershipStatus.Pending ? null : occurredAtUtc,
            UpdatedAtUtc = occurredAtUtc,
        };

    private static CollectiveGoalHostTotal GoalTotal(
        int hostId,
        string bountyPublicId,
        long total,
        DateTime now
    ) =>
        new()
        {
            HostId = hostId,
            SourceBountyPublicId = Guid.Parse(bountyPublicId),
            Total = total,
            LastSourceEventAtUtc = now.AddMinutes(-2),
        };

    private static CollectiveAudit Audit(
        string operationId,
        CollectiveAuditAction action,
        int actingHostId,
        int affectedHostId,
        DateTime occurredAtUtc
    ) =>
        new()
        {
            OperationId = operationId,
            Action = action,
            ActingHostId = actingHostId,
            AffectedHostId = affectedHostId,
            ActorTwitchUserId = SimulationMode.UserId,
            ActorLogin = SimulationMode.Login,
            OccurredAtUtc = occurredAtUtc,
        };

    private static RaidCollaborationHistoryEntry History(
        int hostId,
        string providerMessageId,
        RaidDirection direction,
        string otherTwitchUserId,
        string otherLogin,
        string otherDisplayName,
        int viewerCount,
        string category,
        string streamId,
        DateTime occurredAtUtc,
        RaidWelcomeOutcome welcomeOutcome,
        RaidShoutoutOutcome shoutoutOutcome
    ) =>
        new()
        {
            HostId = hostId,
            ProviderMessageId = providerMessageId,
            Direction = direction,
            OtherTwitchUserId = otherTwitchUserId,
            OtherLogin = otherLogin,
            OtherDisplayName = otherDisplayName,
            ViewerCount = viewerCount,
            Category = category,
            ProviderStreamId = streamId,
            OccurredAtUtc = occurredAtUtc,
            WelcomeOutcome = welcomeOutcome,
            ShoutoutOutcome = shoutoutOutcome,
            RecordedAtUtc = occurredAtUtc,
        };
}
