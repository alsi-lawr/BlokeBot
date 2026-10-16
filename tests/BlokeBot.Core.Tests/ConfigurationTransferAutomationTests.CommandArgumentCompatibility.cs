using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommandArguments_ImportRejectsWiredPluginTypeChangeAndRollsBack(bool enabled)
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await CommandArgumentPluginAsync(database, enabled);
        var changed = CommandArgumentDocument(CustomCommandArgumentKind.Number);
        var outcome = await fixture.Coordinator.ApplyAsync(
            Session(fixture.HostId),
            changed,
            CommandArgumentSelection(fixture.HostId),
            new("destination-id", "destination"),
            CancellationToken.None
        );
        await using var db = await database.CreateDbContextAsync();
        var command = await db.CustomCommands.SingleAsync();
        command
            .ArgumentSchema!.Fields[0]
            .Kind.ShouldBe(
                CustomCommandArgumentKind.Token,
                $"Import outcome: {outcome.GetType().Name}; retained Data edges: {await db.AutomationFlowEdges.CountAsync(edge => edge.Kind == PersistedAutomationEdgeKind.Data)}; audits: {await db.ConfigurationImportAudits.CountAsync()}; persisted plugin provenance: {await db.AutomationFlowNodes.AnyAsync(node => node.Id == fixture.Target.Id.Value && node.PluginProvenanceJson != null)}"
            );
        (
            await db.AutomationFlowEdges.SingleAsync(edge =>
                edge.Kind == PersistedAutomationEdgeKind.Data
            )
        ).TargetNodeId.ShouldBe(fixture.Target.Id.Value);
        _ = (
            await db.AutomationFlowNodes.SingleAsync(node => node.Id == fixture.Target.Id.Value)
        ).PluginProvenanceJson.ShouldNotBeNull();
        (await db.AutomationFlows.SingleAsync()).IsEnabled.ShouldBe(enabled);
        (await db.ConfigurationImportAudits.CountAsync()).ShouldBe(1);
        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommandArguments_ImportChecksRepairedFinalGraphWithRealPluginProvenance(
        bool disconnect
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await CommandArgumentPluginAsync(database, false);
        var scenarios = new AutomationScenarioService(
            database,
            fixture.Catalog,
            fixture.Flows,
            TimeProvider.System,
            new(),
            BotSettings.FromOptions(new BotOptions()),
            OAuthAuthorizationScopeSet.Create(["channel:bot"])
        );
        var exported = (
            await new ConfigurationDocumentExporter(
                database,
                new(),
                fixture.Catalog,
                fixture.Flows,
                TimeProvider.System,
                scenarios
            ).ExportAsync(
                fixture.HostId,
                new(
                    new HashSet<ConfigurationSectionId>
                    {
                        ConfigurationSectionId.CustomCommands,
                        ConfigurationSectionId.Automations,
                    },
                    new(false, false, false)
                )
                {
                    AutomationFlowIds = new HashSet<Guid> { fixture.Draft.Id!.Value.Value },
                },
                CancellationToken.None
            )
        )
            .ShouldBeOfType<ConfigurationExportOutcome.Success>()
            .Document;
        var flow = exported.Sections.Automations!.Flows.Single();
        var source = flow.Nodes.Single(node => node.DefinitionId == "custom-command");
        var plugin = flow.Nodes.Single(node => node.Plugin is not null);
        plugin = plugin with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new { link = "https://example.invalid" }
            ),
            InputBindings = [new("link", AutomationInputBindingMode.Fixed)],
        };
        var delay = Node(
            "delay",
            "delay",
            JsonSerializer.SerializeToElement(
                new Dictionary<string, int> { ["duration-milliseconds"] = 1 }
            ),
            [
                new(
                    AutomationDelayDurationBinding.ValueField.Value,
                    AutomationInputBindingMode.Connected
                ),
            ]
        );
        var repaired = exported with
        {
            Sections = exported.Sections with
            {
                CustomCommands = exported.Sections.CustomCommands! with
                {
                    Commands =
                    [
                        exported.Sections.CustomCommands.Commands.Single() with
                        {
                            ArgumentSchema = new([new("name", CustomCommandArgumentKind.Number)]),
                        },
                    ],
                },
                Automations = exported.Sections.Automations with
                {
                    Flows =
                    [
                        flow with
                        {
                            Nodes = disconnect ? [source, plugin] : [source, delay, plugin],
                            Edges = disconnect
                                ?
                                [
                                    new(
                                        "flow",
                                        AutomationEdgeKind.Flow,
                                        source.Id,
                                        "flow",
                                        plugin.Id,
                                        "flow"
                                    ),
                                ]
                                :
                                [
                                    new(
                                        "flow",
                                        AutomationEdgeKind.Flow,
                                        source.Id,
                                        "flow",
                                        delay.Id,
                                        "flow"
                                    ),
                                    new(
                                        "after",
                                        AutomationEdgeKind.Flow,
                                        delay.Id,
                                        "complete",
                                        plugin.Id,
                                        "flow"
                                    ),
                                    new(
                                        "number",
                                        AutomationEdgeKind.Data,
                                        source.Id,
                                        "argument.name",
                                        delay.Id,
                                        AutomationDelayDurationBinding.Port.Value
                                    ),
                                ],
                        },
                    ],
                },
            },
        };
        var selection = CommandArgumentSelection(fixture.HostId) with
        {
            Sections =
            [
                new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.Merge, []),
                new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, []),
            ],
        };
        var applied = await fixture.Coordinator.ApplyAsync(
            Session(fixture.HostId),
            repaired,
            await ReviewImportEffectsAsync(database, repaired, selection, fixture.Adapter),
            new("destination-id", "destination"),
            CancellationToken.None
        );
        _ = applied.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>(
            JsonSerializer.Serialize(applied)
        );
        await using var db = await database.CreateDbContextAsync();
        var command = await db.CustomCommands.SingleAsync();
        command.ArgumentSchema!.Fields[0].Kind.ShouldBe(CustomCommandArgumentKind.Number);
        (await db.AutomationFlows.SingleAsync()).Id.ShouldBe(fixture.Draft.Id!.Value.Value);
        var importedSource = await db.AutomationFlowNodes.SingleAsync(node =>
            node.DefinitionId == "custom-command"
        );
        using var configuration = JsonDocument.Parse(importedSource.ConfigurationJson);
        configuration.RootElement.GetProperty("custom-command-id").GetInt32().ShouldBe(command.Id);
        var importedPlugin = await db.AutomationFlowNodes.SingleAsync(node =>
            node.DefinitionId == plugin.DefinitionId
        );
        PluginAutomationCatalogRegistry
            .TryDeserializeProvenance(importedPlugin.PluginProvenanceJson, out var provenance)
            .ShouldBeTrue();
        provenance!.SameCode(fixture.Target.Definition.PluginProvenance!).ShouldBeTrue();
        var edges = await db
            .AutomationFlowEdges.Where(edge => edge.Kind == PersistedAutomationEdgeKind.Data)
            .ToArrayAsync();
        if (disconnect)
        {
            edges.ShouldBeEmpty();
        }
        else
        {
            var edge = edges.ShouldHaveSingleItem();
            edge.SourceNodeId.ShouldBe(importedSource.Id);
            var importedDelay = await db.AutomationFlowNodes.SingleAsync(node =>
                node.Id == edge.TargetNodeId
            );
            importedDelay.DefinitionId.ShouldBe("delay");
            edge.TargetPortId.ShouldBe(AutomationDelayDurationBinding.Port.Value);
        }
        (await db.ConfigurationImportAudits.CountAsync()).ShouldBe(2);
        if (!disconnect)
        {
            var restored = await fixture.Coordinator.ApplyAsync(
                Session(fixture.HostId),
                exported,
                await ReviewImportEffectsAsync(database, exported, selection, fixture.Adapter),
                new("destination-id", "destination"),
                CancellationToken.None
            );
            _ = restored.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>(
                JsonSerializer.Serialize(restored)
            );
            await using var final = await database.CreateDbContextAsync();
            (await final.CustomCommands.SingleAsync())
                .ArgumentSchema!.Fields[0]
                .Kind.ShouldBe(CustomCommandArgumentKind.Token);
            var edge = await final.AutomationFlowEdges.SingleAsync(edge =>
                edge.Kind == PersistedAutomationEdgeKind.Data
            );
            var target = await final.AutomationFlowNodes.SingleAsync(node =>
                node.Id == edge.TargetNodeId
            );
            target.DefinitionId.ShouldBe(fixture.Target.Definition.TypeId);
            edge.TargetPortId.ShouldBe("link");
            PluginAutomationCatalogRegistry
                .TryDeserializeProvenance(target.PluginProvenanceJson, out var finalProvenance)
                .ShouldBeTrue();
            finalProvenance!.SameCode(fixture.Target.Definition.PluginProvenance!).ShouldBeTrue();
            (await final.ConfigurationImportAudits.CountAsync()).ShouldBe(3);
        }
    }

    [Test]
    public async Task CommandArguments_UnavailablePluginRefusesChangedWiredTypeButNotStableOrUnusedContracts()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await CommandArgumentPluginAsync(database, false);
        PluginFeatureRevision.TryCreate(2, out var revision).ShouldBeTrue();
        fixture.Snapshots.Publish(
            fixture.State with
            {
                Revision = revision,
                Readiness = new PluginFeatureReadiness.Disabled(),
            }
        );
        _ = fixture
            .Catalog.ValidatePreparedDefinition(
                new(fixture.HostId),
                fixture.Target.Definition,
                fixture.Target.InputBindings
            )
            .ShouldBeOfType<AutomationConfigurationCheck.DefinitionMissing>();
        var stable = CommandArgumentDocument(CustomCommandArgumentKind.Token);
        var original = stable.Sections.CustomCommands!.Commands.Single();
        stable = stable with
        {
            Sections = stable.Sections with
            {
                CustomCommands = stable.Sections.CustomCommands with
                {
                    Commands =
                    [
                        original with
                        {
                            ArgumentSchema = new([
                                new("unused", CustomCommandArgumentKind.Number),
                                new(
                                    "name",
                                    CustomCommandArgumentKind.RemainingText,
                                    MaximumWords: 3
                                ),
                            ]),
                        },
                        original with
                        {
                            Id = "other",
                            Name = "Unrelated",
                            Aliases = ["other"],
                            ArgumentSchema = new([new("value", CustomCommandArgumentKind.Token)]),
                        },
                    ],
                },
            },
        };
        _ = (
            await fixture.Coordinator.ApplyAsync(
                Session(fixture.HostId),
                stable,
                CommandArgumentSelection(fixture.HostId),
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var unusedChange = stable with
        {
            Sections = stable.Sections with
            {
                CustomCommands = stable.Sections.CustomCommands with
                {
                    Commands =
                    [
                        stable.Sections.CustomCommands.Commands[0] with
                        {
                            ArgumentSchema = new([
                                new("unused", CustomCommandArgumentKind.Token),
                                new(
                                    "name",
                                    CustomCommandArgumentKind.RemainingText,
                                    MaximumWords: 3
                                ),
                            ]),
                        },
                        stable.Sections.CustomCommands.Commands[1] with
                        {
                            ArgumentSchema = new([new("value", CustomCommandArgumentKind.Number)]),
                        },
                    ],
                },
            },
        };
        _ = (
            await fixture.Coordinator.ApplyAsync(
                Session(fixture.HostId),
                unusedChange,
                CommandArgumentSelection(fixture.HostId),
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var changed = CommandArgumentDocument(CustomCommandArgumentKind.Number);
        _ = (
            await fixture.Coordinator.ApplyAsync(
                Session(fixture.HostId),
                changed,
                CommandArgumentSelection(fixture.HostId),
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        await using var db = await database.CreateDbContextAsync();
        var commands = await db.CustomCommands.OrderBy(command => command.Id).ToArrayAsync();
        commands[0]
            .ArgumentSchema!.Fields.Single(field => field.Name == "name")
            .Kind.ShouldBe(CustomCommandArgumentKind.RemainingText);
        commands[0]
            .ArgumentSchema!.Fields.Single(field => field.Name == "name")
            .MaximumWords.ShouldBe(3);
        commands[0]
            .ArgumentSchema!.Fields.Single(field => field.Name == "unused")
            .Kind.ShouldBe(CustomCommandArgumentKind.Token);
        commands[1].ArgumentSchema!.Fields[0].Kind.ShouldBe(CustomCommandArgumentKind.Number);
        (
            await db.AutomationFlowEdges.SingleAsync(edge =>
                edge.Kind == PersistedAutomationEdgeKind.Data
            )
        ).TargetNodeId.ShouldBe(fixture.Target.Id.Value);
        (await db.ConfigurationImportAudits.CountAsync()).ShouldBe(3);
    }

    private static ConfigurationDocumentV2 CommandArgumentDocument(
        CustomCommandArgumentKind kind
    ) =>
        new(
            ConfigurationDocumentCodec.Format,
            3,
            DateTimeOffset.UtcNow,
            new("source", "0.19.0"),
            new(
                CustomCommands: new(
                    "UTC",
                    [],
                    [],
                    [
                        new(
                            "command",
                            "Typed plugin",
                            true,
                            ["typed"],
                            true,
                            false,
                            0,
                            CustomCommandCooldownScope.Global,
                            CustomCommandInvocationLimit.Unlimited,
                            new(CustomCommandActionTypeV1.Automation),
                            ArgumentSchema: new([new("name", kind)])
                        ),
                    ]
                )
            )
        );

    private static ConfigurationImportSelection CommandArgumentSelection(int hostId) =>
        new(
            hostId,
            [new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.Merge, [])],
            new HashSet<HostFeatureFlags>()
        );

    private static async Task<CommandArgumentPlugin> CommandArgumentPluginAsync(
        SqliteBlokeBotDbFactory database,
        bool enabled
    )
    {
        var hostId = await SeedHostAsync(database, "typed-plugin");
        await using (var db = await database.CreateDbContextAsync())
        {
            (await db.Hosts.SingleAsync()).EnabledFeatures =
                HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands;
            _ = await db.SaveChangesAsync();
        }
        var manifest = PluginManifestToml
            .Validate(
                PluginContractFixtures.CompleteManifestToml(),
                PluginContractFixtures.CompatibleHost()
            )
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest;
        PluginHostId.TryCreate(hostId, out var host).ShouldBeTrue();
        PluginFeatureId.TryCreate("publishing", out var feature).ShouldBeTrue();
        PluginWorkerGeneration.TryCreate(1, out var worker).ShouldBeTrue();
        PluginFeatureGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        PluginFeatureRevision.TryCreate(1, out var revision).ShouldBeTrue();
        var fence = new PluginLifecycleFence(PluginLifecycleOperationId.New(), worker);
        var registry = new PluginAutomationCatalogRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(automations: registry);
        var snapshots = new PluginFeatureSnapshotRegistry(automations: registry);
        declarations.Publish(manifest, fence);
        var state = new PluginFeatureState(
            new(manifest.Manifest.Id, feature, host),
            fence,
            generation,
            new PluginFeatureReadiness.Ready(),
            revision
        );
        snapshots.Publish(state);
        var features = TestHostFeatureServices.Create(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
        );
        var catalog = new AutomationCatalogService(
            new([new CoreAutomationCatalogModule()], registry),
            features
        );
        var flows = new AutomationFlowService(database, catalog, new(), null!, TimeProvider.System);
        var scenarios = new AutomationScenarioService(
            database,
            catalog,
            flows,
            TimeProvider.System,
            new(),
            BotSettings.FromOptions(new BotOptions()),
            OAuthAuthorizationScopeSet.Create(["channel:bot"])
        );
        var adapter = new AutomationConfigurationTransferAdapter(
            flows,
            catalog,
            TimeProvider.System,
            scenarios
        );
        var overlays = UnavailableOverlayConfigurationTransferAdapter.Instance;
        var writer = new CustomCommandConfigurationGraphWriter(
            database,
            null!,
            TimeProvider.System
        );
        var coordinator = new ConfigurationTransferCoordinator(
            database,
            new(writer, new(), TimeProvider.System),
            new GrantedAuthority(),
            new(),
            TimeProvider.System,
            new RecordingLogger<ConfigurationTransferCoordinator>(),
            TestAutomationUiChanges.Create(),
            StoredValueTestChanges.Create(),
            new(database, overlays, adapter),
            overlays,
            adapter,
            UnavailableConfigurationImportObserverDispatcher.Instance,
            new(1, 1)
        );
        _ = (
            await coordinator.ApplyAsync(
                Session(hostId),
                CommandArgumentDocument(CustomCommandArgumentKind.Token),
                CommandArgumentSelection(hostId),
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        int commandId;
        await using (var db = await database.CreateDbContextAsync())
        {
            commandId = (await db.CustomCommands.SingleAsync()).Id;
        }
        var descriptor = (
            await catalog.DiscoverAsync(new(hostId), CancellationToken.None)
        ).Definitions.Single(value =>
            value.Id.Value.EndsWith(".publish-link", StringComparison.Ordinal)
        );
        var source = CommandArgumentDraftNode(
            "custom-command",
            JsonSerializer.SerializeToElement(
                new Dictionary<string, int> { ["custom-command-id"] = commandId }
            )
        );
        var target = CommandArgumentDraftNode(descriptor.Id.Value, EmptyObject()) with
        {
            Definition = new(descriptor.Id.Value, 1, EmptyObject(), descriptor.PluginProvenance),
            InputBindings = ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationInputBinding
            >.Empty.Add(new("link"), new(AutomationInputBindingMode.Connected, null)),
        };
        var draft = new AutomationFlowDraft(
            null,
            new(hostId),
            "Wired plugin",
            AutomationFlowSchema.CurrentVersion,
            enabled,
            [source, target],
            [
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Flow,
                    source.Id,
                    new("flow"),
                    target.Id,
                    new("flow")
                ),
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Data,
                    source.Id,
                    new("argument.name"),
                    target.Id,
                    new("link")
                ),
            ]
        );
        var saved = (
            await flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        return new(
            hostId,
            catalog,
            flows,
            coordinator,
            snapshots,
            state,
            draft with
            {
                Id = saved.FlowId,
            },
            target,
            adapter
        );
    }

    private static AutomationFlowDraftNode CommandArgumentDraftNode(
        string definition,
        JsonElement configuration
    ) =>
        new(
            new(Guid.NewGuid()),
            new(definition, 1, configuration),
            new(1),
            AutomationNodeFailurePolicy.Stop,
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty
        );

    private sealed record CommandArgumentPlugin(
        int HostId,
        AutomationCatalogService Catalog,
        AutomationFlowService Flows,
        ConfigurationTransferCoordinator Coordinator,
        PluginFeatureSnapshotRegistry Snapshots,
        PluginFeatureState State,
        AutomationFlowDraft Draft,
        AutomationFlowDraftNode Target,
        IAutomationConfigurationTransferAdapter Adapter
    );
}
