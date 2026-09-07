using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    public async Task Format2_NestedClosureAndGeneratedScenariosRoundTripWithoutOperationalRecords()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var first = await SeedHostAsync(database, "portable-first");
        var second = await SeedHostAsync(database, "portable-second");
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = await db.Hosts.ExecuteUpdateAsync(set =>
                set.SetProperty(host => host.EnabledFeatures, HostFeatureFlags.Automations)
            );
        }
        var coordinator = Coordinator(
            database,
            new RecordingLogger<ConfigurationTransferCoordinator>()
        );
        var document = NestedDocument();
        var revisions = document.Sections.Automations!.Subflows;
        document = document with
        {
            Sections = document.Sections with
            {
                Automations = document.Sections.Automations with
                {
                    Subflows =
                    [
                        revisions[0] with
                        {
                            Graph = revisions[0].Graph with
                            {
                                Name = new string(' ', 300) + "Leaf" + new string(' ', 300),
                            },
                        },
                        revisions[1],
                    ],
                },
            },
        };

        var initial = await coordinator.ApplyAsync(
            Session(first),
            document,
            AutomationSelection(first),
            new("destination-id", "destination"),
            CancellationToken.None
        );
        _ = initial.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var transfer = AutomationTransfer(database);
        var library = new AutomationSubflowService(
            database,
            transfer.FlowService,
            TimeProvider.System
        );
        var leafPage = await library.ListAsync(new(first), new("Leaf"), CancellationToken.None);
        leafPage.Subflows.ShouldHaveSingleItem().Name.ShouldBe("Leaf");
        var scenarioService = new AutomationScenarioService(
            database,
            transfer.Catalog,
            transfer.FlowService,
            TimeProvider.System
        );
        AutomationFlowDraft sourceDraft;
        await using (var db = await database.CreateDbContextAsync())
        {
            var row = await db
                .AutomationFlows.Include(flow => flow.Nodes)
                .Include(flow => flow.Edges)
                .SingleAsync();
            sourceDraft = AutomationFlowService
                .RestoreDraft(row)
                .ShouldBeOfType<AutomationFlowDraftRestoreOutcome.Available>()
                .Draft;
        }
        var source = sourceDraft.Nodes.Single(node =>
            node.Definition.TypeId == AutomationDefinitionIds.StreamOnlineSource.Value
        );
        var send = sourceDraft.Nodes.Single(node =>
            node.Definition.TypeId == AutomationDefinitionIds.SendChatAction.Value
        );
        var fixture = scenarioService.CreatePortableFixture(
            sourceDraft,
            source.Id,
            42,
            inputs:
            [
                new(
                    send.Id,
                    new("message"),
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    AutomationDataSensitivity.Safe,
                    AutomationValueProvenance.Generated
                ),
            ],
            effects: [new(send.Id, AutomationScenarioEffectResult.Failed)]
        );
        var saved = await scenarioService.SaveAsync(
            new(first),
            sourceDraft.Id!.Value,
            null,
            "Generated scenario",
            fixture,
            CancellationToken.None
        );
        saved.Status.ShouldBe(
            AutomationScenarioAuthoringStatus.Saved,
            string.Join(
                " | ",
                saved.Errors.IsDefault ? [] : saved.Errors.Select(error => error.Message)
            )
        );
        _ = (
            await scenarioService.RunAsync(sourceDraft, fixture, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Failed>();
        var firstExport = await ExportAllAsync(first);
        var section = firstExport.Document.Sections.Automations!;
        section.Subflows.Count.ShouldBe(2);
        section.Scenarios.Count.ShouldBe(1);
        var secondImport = await coordinator.ApplyAsync(
            Session(second),
            firstExport.Document,
            AutomationSelection(second),
            new("destination-id", "destination"),
            CancellationToken.None
        );
        _ = secondImport.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var secondExport = await ExportAllAsync(second);
        JsonSerializer
            .Serialize(secondExport.Document.Sections)
            .ShouldBe(JsonSerializer.Serialize(firstExport.Document.Sections));
        var again = await coordinator.ApplyAsync(
            Session(second),
            firstExport.Document,
            AutomationSelection(second),
            new("destination-id", "destination"),
            CancellationToken.None
        );
        _ = again.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var skippedDocument = firstExport.Document with
        {
            Sections = firstExport.Document.Sections with
            {
                Automations = section with
                {
                    Subflows =
                    [
                        .. section.Subflows.Select(revision =>
                            revision with
                            {
                                Description = "Unapplied change",
                            }
                        ),
                    ],
                },
            },
        };
        _ = (
            await coordinator.ApplyAsync(
                Session(second),
                skippedDocument,
                AutomationSelection(second) with
                {
                    Sections =
                    [
                        new(
                            ConfigurationSectionId.Automations,
                            ImportConflictStrategy.AddMissing,
                            []
                        ),
                    ],
                },
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        await using (var db = await database.CreateDbContextAsync())
        {
            (await db.AutomationFlows.CountAsync()).ShouldBe(2);
            (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(4);
            (await db.AutomationScenarios.CountAsync()).ShouldBe(2);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
            (await db.AutomationTraces.CountAsync()).ShouldBe(1);
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
            var secondFlow = await db.AutomationFlows.SingleAsync(flow => flow.HostId == second);
            secondFlow.Id.ShouldNotBe(sourceDraft.Id.Value.Value);
        }
        var copied = fixture with
        {
            Context = fixture.Context with
            {
                Stream = fixture.Context.Stream! with { Title = "private-live-title" },
            },
        };
        var changed = await scenarioService.SaveAsync(
            new(first),
            sourceDraft.Id.Value,
            saved.Id,
            "Generated scenario",
            copied,
            CancellationToken.None
        );
        changed.Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        var rejected = await ExportSelectedAsync(
            database,
            transfer,
            first,
            [saved.Id!.Value.Value]
        );
        _ = rejected.ShouldBeOfType<ConfigurationExportOutcome.Unsupported>();
        var retained = await scenarioService.ListAsync(
            new(first),
            sourceDraft.Id.Value,
            CancellationToken.None
        );
        retained.Single().Fixture.Context.Stream!.Title.ShouldBe("private-live-title");
        var withoutScenario = await ExportSelectedAsync(database, transfer, first, []);
        _ = withoutScenario.ShouldBeOfType<ConfigurationExportOutcome.Success>();

        async Task<ConfigurationExportOutcome.Success> ExportAllAsync(int hostId)
        {
            await using var db = await database.CreateDbContextAsync();
            var scenarios = await db
                .AutomationScenarios.Where(scenario =>
                    db.AutomationFlows.Any(flow =>
                        flow.Id == scenario.FlowId && flow.HostId == hostId
                    )
                )
                .Select(scenario => scenario.Id)
                .ToArrayAsync();
            return (
                await ExportSelectedAsync(database, transfer, hostId, scenarios)
            ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        }
    }

    [Test]
    public async Task Format2_DependencyFailureRejectsWholeImportAndLeavesExistingGraphsUntouched()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "reject-dependencies");
        var coordinator = Coordinator(
            database,
            new RecordingLogger<ConfigurationTransferCoordinator>()
        );
        var document = NestedDocument();
        var section = document.Sections.Automations!;
        var invalid = section with { Subflows = [section.Subflows[0]] };
        var outcome = await coordinator.ApplyAsync(
            Session(host),
            document with
            {
                Sections = document.Sections with { Automations = invalid },
            },
            AutomationSelection(host),
            new("destination-id", "destination"),
            CancellationToken.None
        );
        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        var cycle = section.Subflows[0] with
        {
            Graph = section.Subflows[0].Graph with
            {
                Nodes =
                [
                    .. section.Subflows[0].Graph.Nodes.Take(1),
                    Node("recursive", AutomationSubflowDefinitions.Invoke, EmptyObject()) with
                    {
                        Subflow = Binding("outer"),
                    },
                    .. section.Subflows[0].Graph.Nodes.Skip(1),
                ],
            },
        };
        outcome = await coordinator.ApplyAsync(
            Session(host),
            document with
            {
                Sections = document.Sections with
                {
                    Automations = section with { Subflows = [cycle, section.Subflows[1]] },
                },
            },
            AutomationSelection(host),
            new("destination-id", "destination"),
            CancellationToken.None
        );
        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        await using var db = await database.CreateDbContextAsync();
        (await db.AutomationFlows.CountAsync()).ShouldBe(0);
        (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(0);
        (await db.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Format2_InvalidGraphAndCopiedIdentityRejectWithoutPersistingRawValues()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "privacy");
        var valid = OrdinaryAutomationDocument(
            "Invalid",
            JsonSerializer.SerializeToElement(new { message = "hello" })
        );
        var graph = valid.Sections.Automations!.Flows[0];
        var invalid = valid with
        {
            Sections = valid.Sections with
            {
                Automations = valid.Sections.Automations with
                {
                    Flows =
                    [
                        graph with
                        {
                            Edges =
                            [
                                .. graph.Edges,
                                new(
                                    "cycle",
                                    AutomationEdgeKind.Flow,
                                    "action",
                                    "complete",
                                    "source",
                                    "flow"
                                ),
                            ],
                        },
                    ],
                },
            },
        };
        var coordinator = Coordinator(
            database,
            new RecordingLogger<ConfigurationTransferCoordinator>()
        );
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                invalid,
                AutomationSelection(host),
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        var identity = DynamicFixedValueDocument(
            "Identity",
            AutomationPortValueType.Actor,
            new Dictionary<string, string>
            {
                { "login", "real-viewer" },
                { "display-name", "Private Viewer" },
            }
        );
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                identity,
                AutomationSelection(host),
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        await using var db = await database.CreateDbContextAsync();
        (await db.AutomationFlows.CountAsync()).ShouldBe(0);
        (await db.AutomationFlowNodes.CountAsync()).ShouldBe(0);
        (await db.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Format2_LateAuditFailureRollsBackSubflowsScenariosAndActivationTogether()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(
            new FailImportAuditSaveInterceptor()
        );
        var host = await SeedHostAsync(database, "atomic-subflows");
        var document = NestedDocument();
        document = document with
        {
            Sections = document.Sections with
            {
                Automations = document.Sections.Automations! with
                {
                    Scenarios =
                    [
                        new(
                            "scenario",
                            "flow",
                            "Generated",
                            "source",
                            AutomationDefinitionIds.StreamOnlineSource.Value,
                            1,
                            new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
                            0,
                            [],
                            []
                        ),
                    ],
                },
                ChannelToolEnablement = ChannelToolEnablementMapper.FromFlags(
                    HostFeatureFlags.Automations
                ),
            },
        };
        var selection = AutomationSelection(host) with
        {
            Sections =
            [
                .. AutomationSelection(host).Sections,
                new(ConfigurationSectionId.ChannelToolEnablement, ImportConflictStrategy.Merge, []),
            ],
            EnablementChanges = new HashSet<HostFeatureFlags> { HostFeatureFlags.Automations },
        };
        var outcome = await Coordinator(
                database,
                new RecordingLogger<ConfigurationTransferCoordinator>()
            )
            .ApplyAsync(
                Session(host),
                document,
                selection,
                new("destination-id", "destination"),
                CancellationToken.None
            );
        if (outcome is ConfigurationImportApplyOutcome.Invalid invalid)
        {
            throw new InvalidOperationException(
                string.Join(" | ", invalid.Issues.Select(issue => issue.Message))
            );
        }
        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Failed>();
        await using var db = await database.CreateDbContextAsync();
        (await db.AutomationFlows.CountAsync()).ShouldBe(0);
        (await db.AutomationSubflows.CountAsync()).ShouldBe(0);
        (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(0);
        (await db.AutomationSubflowNestedCallers.CountAsync()).ShouldBe(0);
        (await db.AutomationSubflowCallers.CountAsync()).ShouldBe(0);
        (await db.AutomationScenarios.CountAsync()).ShouldBe(0);
        (await db.ConfigurationActivations.CountAsync()).ShouldBe(0);
        (await db.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
        (await db.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.None);
    }

    [Test]
    public async Task Format2_DisabledStorageDoesNotActivateAndEnabledCallerRequiresResultingFeatures()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "disabled-storage");
        var document = NestedDocument();
        var coordinator = Coordinator(
            database,
            new RecordingLogger<ConfigurationTransferCoordinator>()
        );
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                AutomationSelection(host),
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var section = document.Sections.Automations!;
        document = document with
        {
            Sections = document.Sections with
            {
                Automations = section with { Flows = [section.Flows[0] with { Enabled = true }] },
            },
        };
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                AutomationSelection(host),
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        await using var db = await database.CreateDbContextAsync();
        (await db.AutomationFlows.SingleAsync()).IsEnabled.ShouldBeFalse();
        (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(2);
        (await db.ConfigurationImportAudits.CountAsync()).ShouldBe(1);
        (await db.ConfigurationActivations.CountAsync()).ShouldBe(0);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        _ = await db.AutomationFlows.ExecuteUpdateAsync(set =>
            set.SetProperty(flow => flow.Name, "Renamed locally")
        );
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                NestedDocument(),
                AutomationSelection(host),
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        (await db.AutomationFlows.AsNoTracking().SingleAsync()).Name.ShouldBe("Renamed locally");
    }

    [Test]
    public async Task Format2_ReleasedDatabaseUpgradePreservesAuthoredFlowAndAdmittedFrozenRun()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await using (var db = await database.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync("20260822192152_v0.12.0_GuessingSharedAliases");
        }
        var host = await SeedHostAsync(database, "released-upgrade");
        var flowId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        const string Frozen = "{\"frozen\":\"admitted-before-upgrade\"}";
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO automation_flows (Id,HostId,Name,SchemaVersion,IsEnabled,CreatedAtUtc,UpdatedAtUtc) VALUES ({flowId},{host},{"Released flow"},1,0,{now},{now})"
            );
            _ = await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO automation_flow_runs (Id,FlowId,HostId,AutomationGeneration,RequiredFeatures,ContextSchemaVersion,SourceDefinitionId,SourceOccurrenceId,ContextJson,DefinitionJson,Status,StartedAtUtc) VALUES ({runId},{flowId},{host},0,0,1,{AutomationDefinitionIds.StreamOnlineSource.Value},{Guid.NewGuid()},{"{}"},{Frozen},{"Waiting"},{now})"
            );
            await db.Database.MigrateAsync();
        }
        await using (var db = await database.CreateDbContextAsync())
        {
            (await db.AutomationFlows.SingleAsync()).Id.ShouldBe(flowId);
            var run = await db.AutomationFlowRuns.SingleAsync();
            run.Id.ShouldBe(runId);
            run.DefinitionJson.ShouldBe(Frozen);
            run.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
        }
    }

    private static async Task<ConfigurationExportOutcome> ExportSelectedAsync(
        SqliteBlokeBotDbFactory database,
        AutomationTransferComponents transfer,
        int hostId,
        IEnumerable<Guid> scenarioIds
    ) =>
        await new ConfigurationDocumentExporter(
            database,
            new(),
            transfer.Catalog,
            transfer.FlowService,
            TimeProvider.System,
            new(database, transfer.Catalog, transfer.FlowService, TimeProvider.System)
        ).ExportAsync(
            hostId,
            new(
                new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Automations },
                new(false, false, false)
            )
            {
                AutomationFlowIds = await SelectedFlowIdsAsync(database, hostId),
                AutomationScenarioIds = scenarioIds.ToHashSet(),
            },
            CancellationToken.None
        );

    private static AutomationSubflowBindingV2 Binding(string? revision) =>
        new(
            revision,
            new([], []),
            AutomationDataValueSerialization.SerializeOutputs(
                new Dictionary<AutomationPortId, AutomationResolvedValue>()
            )
        );

    private static ConfigurationDocumentV2 NestedDocument()
    {
        var leaf = new AutomationFlowV2(
            "leaf",
            "Leaf",
            false,
            1,
            AutomationFlowOrientation.Horizontal,
            AutomationEdgeStyle.Angular,
            [
                Node("entry", AutomationSubflowDefinitions.Entry, EmptyObject()) with
                {
                    Subflow = Binding(null),
                },
                Node("exit", AutomationSubflowDefinitions.Exit, EmptyObject()) with
                {
                    Subflow = Binding(null),
                },
            ],
            [new("edge", AutomationEdgeKind.Flow, "entry", "complete", "exit", "flow")]
        );
        var outer = leaf with
        {
            Id = "outer",
            Name = "Outer",
            Nodes =
            [
                leaf.Nodes[0],
                Node("invoke", AutomationSubflowDefinitions.Invoke, EmptyObject()) with
                {
                    Subflow = Binding("leaf"),
                },
                leaf.Nodes[1],
            ],
            Edges =
            [
                new("a", AutomationEdgeKind.Flow, "entry", "complete", "invoke", "flow"),
                new("b", AutomationEdgeKind.Flow, "invoke", "complete", "exit", "flow"),
            ],
        };
        var doc = AutomationDocument(
            "Nested caller",
            [
                Node("source", AutomationDefinitionIds.StreamOnlineSource.Value, EmptyObject()),
                Node("invoke", AutomationSubflowDefinitions.Invoke, EmptyObject()) with
                {
                    Subflow = Binding("outer"),
                },
            ],
            [new("edge", AutomationEdgeKind.Flow, "source", "flow", "invoke", "flow")]
        );
        var caller = doc.Sections.Automations!.Flows[0];
        caller = caller with
        {
            Nodes =
            [
                .. caller.Nodes,
                Node(
                    "value",
                    AutomationDefinitionIds.CelTransform.Value,
                    TransformConfiguration([], [TransformOutput("text-output", "Text", "'hello'")])
                ),
                Node(
                    "send",
                    AutomationDefinitionIds.SendChatAction.Value,
                    JsonSerializer.SerializeToElement(new { message = "fallback" }),
                    [new("message", AutomationInputBindingMode.Connected)]
                ),
            ],
            Edges =
            [
                .. caller.Edges,
                new("send-flow", AutomationEdgeKind.Flow, "invoke", "complete", "send", "flow"),
                new(
                    "send-data",
                    AutomationEdgeKind.Data,
                    "value",
                    "text-output",
                    "send",
                    "message"
                ),
            ],
        };
        doc = doc with
        {
            Sections = doc.Sections with
            {
                Automations = doc.Sections.Automations with { Flows = [caller] },
            },
        };
        return doc with
        {
            Sections = doc.Sections with
            {
                Automations = doc.Sections.Automations! with
                {
                    Subflows =
                    [
                        new("leaf", "Leaf", new([], []), leaf),
                        new("outer", "Outer", new([], []), outer),
                    ],
                },
            },
        };
    }
}
