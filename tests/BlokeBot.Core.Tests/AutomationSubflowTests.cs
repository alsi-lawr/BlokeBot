using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task Subflow_PublicationPinsMultipleCallersAndOnlyExplicitCompatibleRebindMovesOne()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var draft = Subflow(fixture.HostId);
        var first = await Publish(service, draft);
        var callerA = Caller(fixture.HostId, first);
        var callerB = Caller(fixture.HostId, first);
        var idA = (await fixture.Flows.SaveAsync(callerA, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var idB = (await fixture.Flows.SaveAsync(callerB, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var compatible = await Publish(service, draft with { Description = "Revised description" });
        (
            await service.RemoveRevisionAsync(new(fixture.HostId), first.Id, CancellationToken.None)
        ).ShouldBe(AutomationSubflowRemovalOutcome.Referenced);
        var changedInterface = new AutomationSubflowInterface(
            [SubflowPort("text", AutomationPortValueType.Text)],
            [SubflowPort("text", AutomationPortValueType.Text)]
        );
        var changed = Subflow(fixture.HostId, changedInterface) with { Id = draft.Id };
        var publication = (
            await service.PublishAsync(changed, CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Published>();
        publication
            .IncompatibleCallers.Select(caller => caller.FlowId)
            .ShouldBe([idA, idB], ignoreOrder: true);
        var rebind = callerA with
        {
            Id = idA,
            Nodes =
            [
                .. callerA.Nodes.Select(node =>
                    node.Definition.TypeId == AutomationSubflowDefinitions.Invoke
                        ? node with
                        {
                            Definition = AutomationSubflowDefinitions.Invocation(compatible),
                        }
                        : node
                ),
            ],
        };
        _ = (
            await fixture.Flows.SaveAsync(rebind, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var invalidRebind = rebind with
        {
            Nodes =
            [
                .. rebind.Nodes.Select(node =>
                    node.Definition.TypeId == AutomationSubflowDefinitions.Invoke
                        ? Invoke(
                            publication.Revision,
                            node.Id,
                            new Dictionary<AutomationPortId, AutomationValue>
                            {
                                [new("text")] = new AutomationValue.Text("value"),
                            }.ToImmutableDictionary()
                        )
                        : node
                ),
            ],
        };
        (await fixture.Flows.SaveAsync(invalidRebind, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "subflow-rebind-incompatible");
        await using var db = await fixture.Database.CreateDbContextAsync();
        var pins = await db.AutomationSubflowCallers.AsNoTracking().ToArrayAsync();
        pins.Single(pin => pin.NodeId == callerA.Nodes[1].Id.Value)
            .RevisionId.ShouldBe(compatible.Id.Value);
        pins.Single(pin => pin.NodeId == callerB.Nodes[1].Id.Value)
            .RevisionId.ShouldBe(first.Id.Value);
        _ = (
            await fixture.Flows.DeleteAsync(new(fixture.HostId), idB, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowDeleteOutcome.Deleted>();
        (
            await service.RemoveRevisionAsync(new(fixture.HostId), first.Id, CancellationToken.None)
        ).ShouldBe(AutomationSubflowRemovalOutcome.Removed);
    }

    [Test]
    public async Task Subflow_TypedPortsAndImmutableClosureSurviveServiceRestartAndRejectForgedCallerPorts()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var ports = ImmutableArray.Create(
            SubflowPort("text", AutomationPortValueType.Text),
            SubflowPort("array", AutomationPortValueType.Array),
            SubflowPort("map", AutomationPortValueType.Map),
            SubflowPort("nullable", AutomationPortValueType.Number) with
            {
                Nullability = AutomationPortNullability.Nullable,
            }
        );
        var service = Subflows(fixture);
        var revision = await Publish(service, Subflow(fixture.HostId, new(ports, ports)));
        var values = new Dictionary<AutomationPortId, AutomationValue>
        {
            [new("text")] = new AutomationValue.Text("hello"),
            [new("array")] = new AutomationValue.Array([
                new AutomationValue.Number(42),
                new AutomationValue.Boolean(true),
            ]),
            [new("map")] = new AutomationValue.Map([
                new("nested", new AutomationValue.Array([new AutomationValue.Text("value")])),
            ]),
            [new("nullable")] = new AutomationValue.Null(AutomationPortValueType.Number),
        }.ToImmutableDictionary();
        var caller = Caller(fixture.HostId, revision, values);
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var restored = (
            await Subflows(fixture).ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldHaveSingleItem();
        AutomationSubflowSerialization
            .Serialize(restored)
            .ShouldBe(AutomationSubflowSerialization.Serialize(revision));
        var closure = (
            await service.LoadClosureAsync(
                new(fixture.HostId),
                [revision.Id],
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationSubflowClosureOutcome.Available>();
        AutomationSubflowSerialization
            .Serialize(closure.Closure.Revisions.Single())
            .ShouldBe(AutomationSubflowSerialization.Serialize(revision));
        var forged = revision with { Interface = new([], []) };
        var forgedCaller = Caller(fixture.HostId, forged);
        (await fixture.Flows.SaveAsync(forgedCaller, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "subflow-pin-invalid");
        var wrongValue = Caller(
            fixture.HostId,
            revision,
            values.SetItem(new("map"), new AutomationValue.Text("not a map"))
        );
        (await fixture.Flows.SaveAsync(wrongValue, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "subflow-fixed-type");
    }

    [Test]
    public async Task Subflow_HostIsolationRejectsCrossHostPinsAndLeavesBothCataloguesUnchanged()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var otherHost = await fixture.SeedHostAsync(
            "other",
            HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands
        );

        var service = Subflows(fixture);
        var revision = await Publish(service, Subflow(fixture.HostId));
        _ = (
            await service.LoadClosureAsync(new(otherHost), [revision.Id], CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowClosureOutcome.Invalid>();
        _ = (
            await fixture.Flows.SaveAsync(Caller(otherHost, revision), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        _ = (
            await service.PublishAsync(
                Nesting(Subflow(otherHost), revision),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>();
        (
            await service.RemoveRevisionAsync(new(otherHost), revision.Id, CancellationToken.None)
        ).ShouldBe(AutomationSubflowRemovalOutcome.NotFound);
        (await service.ListAsync(new(otherHost), CancellationToken.None)).ShouldBeEmpty();
        (await service.ListAsync(new(fixture.HostId), CancellationToken.None)).Length.ShouldBe(1);
    }

    [Test]
    public async Task Subflow_RecursionAndDepthAreRejectedWithoutPublishingPartialRevisions()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var a = Subflow(fixture.HostId);
        var first = await Publish(service, a);
        var direct = (
            await service.PublishAsync(Nesting(a, first), CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>();
        direct.Errors.ShouldContain(error => error.Code == "subflow-recursion");
        var b = await Publish(service, Nesting(Subflow(fixture.HostId), first));
        (await service.PublishAsync(Nesting(a, b), CancellationToken.None))
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "subflow-recursion");
        var current = b;
        for (var depth = 2; depth < AutomationSubflowStore.MaximumDepth; depth++)
        {
            current = await Publish(service, Nesting(Subflow(fixture.HostId), current));
        }
        (
            await service.PublishAsync(
                Nesting(Subflow(fixture.HostId), current),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "subflow-depth");
        (await service.ListAsync(new(fixture.HostId), CancellationToken.None)).Length.ShouldBe(
            AutomationSubflowStore.MaximumDepth
        );
        (
            await service.RemoveRevisionAsync(new(fixture.HostId), first.Id, CancellationToken.None)
        ).ShouldBe(AutomationSubflowRemovalOutcome.Referenced);
    }

    [Test]
    public async Task Subflow_GraphValidationPreservesNormalSourceRulesAndRejectsDisconnectedOrMistypedBoundaries()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var draft = Subflow(
            fixture.HostId,
            new(
                [SubflowPort("value", AutomationPortValueType.Text)],
                [SubflowPort("value", AutomationPortValueType.Text)]
            )
        );
        (await fixture.Flows.SaveAsync(draft.Graph, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "source-count");
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var invalid = draft with
        {
            Graph = draft.Graph with { Nodes = [.. draft.Graph.Nodes, source] },
        };
        (await service.PublishAsync(invalid, CancellationToken.None))
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "subflow-boundaries");
        invalid = draft with
        {
            Graph = draft.Graph with
            {
                Edges = [.. draft.Graph.Edges.Where(edge => edge.Kind != AutomationEdgeKind.Flow)],
            },
        };
        _ = (
            await service.PublishAsync(invalid, CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>();
        var incompatible = new AutomationSubflowInterface(
            draft.Interface.Inputs,
            [SubflowPort("value", AutomationPortValueType.Map)]
        );
        invalid = Subflow(fixture.HostId, incompatible);
        (await service.PublishAsync(invalid, CancellationToken.None))
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "data-type-incompatible");
        invalid = Subflow(
            fixture.HostId,
            new([draft.Interface.Inputs[0], draft.Interface.Inputs[0]], [])
        );
        _ = (
            await service.PublishAsync(invalid, CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>();
        (await service.ListAsync(new(fixture.HostId), CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task Subflow_FrozenRunReferencesPersistAcrossRestartAndFenceRemovalUntilRetired()
    {
        await using var fixture = await RuntimeFixture.CreateAsync(migrateSchema: true);
        var service = Subflows(fixture);
        var leaf = await Publish(service, Subflow(fixture.HostId));
        var parent = await Publish(service, Nesting(Subflow(fixture.HostId), leaf));
        var flowId = (
            await fixture.Flows.SaveAsync(
                Draft(fixture.HostId, [Node("custom-command", """{"custom-command-id":7}""")], []),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var closure = (
            await service.LoadClosureAsync(new(fixture.HostId), [parent.Id], CancellationToken.None)
        )
            .ShouldBeOfType<AutomationSubflowClosureOutcome.Available>()
            .Closure;
        var runId = new AutomationRunId(Guid.NewGuid());
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            _ = db.AutomationFlowRuns.Add(
                new()
                {
                    Id = runId.Value,
                    HostId = fixture.HostId,
                    FlowId = flowId.Value,
                    Status = AutomationFlowRunStatus.Waiting,
                }
            );
            (
                await AutomationSubflowRunReferences.AttachAsync(
                    db,
                    new(fixture.HostId),
                    runId,
                    closure,
                    CancellationToken.None
                )
            ).ShouldBeEmpty();
            _ = await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        (
            await service.RemoveRevisionAsync(
                new(fixture.HostId),
                parent.Id,
                CancellationToken.None
            )
        ).ShouldBe(AutomationSubflowRemovalOutcome.Referenced);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var frozen = await AutomationSubflowRunReferences.LoadAsync(
                db,
                new(fixture.HostId),
                runId,
                CancellationToken.None
            );
            frozen
                .Revisions.Select(AutomationSubflowSerialization.Serialize)
                .ShouldBe(
                    closure.Revisions.Select(AutomationSubflowSerialization.Serialize),
                    ignoreOrder: true
                );
            (
                await AutomationSubflowRunReferences.AttachAsync(
                    db,
                    new(fixture.HostId),
                    runId,
                    new([leaf]),
                    CancellationToken.None
                )
            ).ShouldNotBeEmpty();
            _ = await AutomationSubflowRunReferences.RetireAsync(
                db,
                new(fixture.HostId),
                runId,
                CancellationToken.None
            );
            await transaction.CommitAsync();
        }
        (
            await service.RemoveRevisionAsync(
                new(fixture.HostId),
                parent.Id,
                CancellationToken.None
            )
        ).ShouldBe(AutomationSubflowRemovalOutcome.Removed);
        (
            await service.RemoveRevisionAsync(new(fixture.HostId), leaf.Id, CancellationToken.None)
        ).ShouldBe(AutomationSubflowRemovalOutcome.Removed);
    }

    [Test]
    public async Task Subflow_TransitiveFeatureAndPluginDependenciesStayExplicitAndUnavailableDefinitionsRejectCallerSaves()
    {
        await using var fixture = await RuntimeFixture.CreateAsync(
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Overlays
        );
        var manifest = PluginManifestToml
            .Validate(
                PluginContractFixtures.CompleteManifestToml(),
                PluginContractFixtures.CompatibleHost()
            )
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest;
        PluginHostId.TryCreate(fixture.HostId, out var pluginHost).ShouldBeTrue();
        PluginFeatureId.TryCreate("publishing", out var featureId).ShouldBeTrue();
        PluginWorkerGeneration.TryCreate(1, out var worker).ShouldBeTrue();
        PluginFeatureGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        PluginFeatureRevision.TryCreate(1, out var stateRevision).ShouldBeTrue();
        var fence = new PluginLifecycleFence(PluginLifecycleOperationId.New(), worker);
        var registry = new PluginAutomationCatalogRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(automations: registry);
        var snapshots = new PluginFeatureSnapshotRegistry(automations: registry);
        declarations.Publish(manifest, fence);
        var state = new PluginFeatureState(
            new(manifest.Manifest.Id, featureId, pluginHost),
            fence,
            generation,
            new PluginFeatureReadiness.Ready(),
            stateRevision
        );
        snapshots.Publish(state);
        var definitions = new AutomationDefinitionCatalog(
            [new CoreAutomationCatalogModule()],
            registry
        );
        var catalog = new AutomationCatalogService(
            definitions,
            fixture.Features,
            fixture.Expressions
        );
        var flows = new AutomationFlowService(
            fixture.Database,
            catalog,
            fixture.Expressions,
            new NoOverlayCues(),
            fixture.Clock
        );
        var service = new AutomationSubflowService(fixture.Database, flows, fixture.Clock);
        var available = await catalog.DiscoverAsync(new(fixture.HostId), CancellationToken.None);
        var descriptor = available.Definitions.Single(value =>
            value.Id.Value.EndsWith(".publish-link", StringComparison.Ordinal)
        );
        var plugin = Node(
            descriptor.Id.Value,
            """{"link":"https://example.invalid"}""",
            bindings: Bindings("link", AutomationInputBindingMode.Fixed)
        ) with
        {
            Definition = new(
                descriptor.Id.Value,
                1,
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new { link = "https://example.invalid" }
                ),
                descriptor.PluginProvenance
            ),
        };
        var draft = Subflow(fixture.HostId);
        draft = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [draft.Graph.Nodes[0], plugin, draft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(draft.Graph.Nodes[0], "complete", plugin),
                    Edge(plugin, "complete", draft.Graph.Nodes[1]),
                ],
            },
        };
        var leaf = await Publish(service, draft);
        var stalePlugin = plugin with
        {
            Definition = plugin.Definition with
            {
                PluginProvenance = descriptor.PluginProvenance! with
                {
                    DefinitionHash = "changed-code",
                },
            },
        };
        _ = (
            await service.PublishAsync(
                draft with
                {
                    Graph = draft.Graph with
                    {
                        Nodes = [draft.Graph.Nodes[0], stalePlugin, draft.Graph.Nodes[2]],
                    },
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>();
        var parent = await Publish(service, Nesting(Subflow(fixture.HostId), leaf));
        parent.PluginDependencies.ShouldHaveSingleItem().ShouldBe(descriptor.PluginProvenance);
        parent.RequiredFeatures.HasFlag(HostFeatureFlags.Automations).ShouldBeTrue();
        _ = (
            await flows.SaveAsync(Caller(fixture.HostId, parent), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        PluginFeatureRevision.TryCreate(2, out var disabledRevision).ShouldBeTrue();
        snapshots.Publish(
            state with
            {
                Readiness = new PluginFeatureReadiness.Disabled(),
                Revision = disabledRevision,
            }
        );
        _ = (
            await flows.SaveAsync(Caller(fixture.HostId, parent), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        var restored = (
            await service.LoadClosureAsync(new(fixture.HostId), [parent.Id], CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowClosureOutcome.Available>();
        restored
            .Closure.Revisions.Single(value => value.Id == parent.Id)
            .PluginDependencies.ShouldBe(parent.PluginDependencies);

        var overlays = new HostBoundOverlayCues();
        var targetId = Guid.NewGuid();
        var cueId = Guid.NewGuid();
        overlays.AddTarget(fixture.HostId, targetId, OverlayType.CuePlayer);
        overlays.AddCue(fixture.HostId, cueId, OverlayCueQueuePolicy.Replace);
        var overlayService = new AutomationSubflowService(
            fixture.Database,
            new AutomationFlowService(
                fixture.Database,
                fixture.Catalog,
                fixture.Expressions,
                overlays,
                fixture.Clock
            ),
            fixture.Clock
        );
        var overlayDraft = Subflow(fixture.HostId);
        var overlay = Node(
            "play-overlay-cue",
            $$"""{"target-id":"{{targetId}}","cue-id":"{{cueId}}"}"""
        );
        overlayDraft = overlayDraft with
        {
            Graph = overlayDraft.Graph with
            {
                Nodes = [overlayDraft.Graph.Nodes[0], overlay, overlayDraft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(overlayDraft.Graph.Nodes[0], "complete", overlay),
                    Edge(overlay, "complete", overlayDraft.Graph.Nodes[1]),
                ],
            },
        };
        var overlayRevision = await Publish(overlayService, overlayDraft);
        overlayRevision.RequiredFeatures.HasFlag(HostFeatureFlags.Overlays).ShouldBeTrue();
        _ = await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Overlays,
            CancellationToken.None
        );
        (
            await fixture.Flows.SaveAsync(
                Caller(fixture.HostId, overlayRevision),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "capability-unavailable");
    }

    [Test]
    public async Task Subflow_OutputBindingRequiresInvocationOnEveryFlowPathAndRejectsASecondTerminalPath()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var port = SubflowPort("value", AutomationPortValueType.Text);
        var revision = await Publish(service, Subflow(fixture.HostId, new([port], [port])));
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var invoke = Invoke(
            revision,
            values: new Dictionary<AutomationPortId, AutomationValue>
            {
                [port.Id] = new AutomationValue.Text("hello"),
            }.ToImmutableDictionary()
        );
        var send = Node(
            "send-chat",
            """{"message":"connected input"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var graph = Draft(
            fixture.HostId,
            [source, invoke, send],
            [
                Edge(source, "flow", invoke),
                Edge(invoke, "complete", send),
                Edge(invoke, "value", send, "message", AutomationEdgeKind.Data),
            ]
        );
        var saved = (
            await fixture.Flows.SaveAsync(graph, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var condition = Node("condition", """{"predicate":true}""");
        var bypass = graph with
        {
            Id = saved.FlowId,
            Nodes = [source, condition, invoke, send],
            Edges =
            [
                Edge(source, "flow", condition),
                Edge(condition, "yes", invoke),
                Edge(condition, "no", send),
                Edge(invoke, "complete", send),
                Edge(invoke, "value", send, "message", AutomationEdgeKind.Data),
            ],
        };
        (await fixture.Flows.SaveAsync(bypass, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "subflow-output-unavailable");
        var draft = Subflow(fixture.HostId);
        var unfinished = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [draft.Graph.Nodes[0], condition, draft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(draft.Graph.Nodes[0], "complete", condition),
                    Edge(condition, "yes", draft.Graph.Nodes[1]),
                ],
            },
        };
        (await service.PublishAsync(unfinished, CancellationToken.None))
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "subflow-flow-output-disconnected");
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationSubflowCallers.CountAsync()).ShouldBe(1);
        (
            await db.AutomationFlowNodes.CountAsync(node => node.FlowId == saved.FlowId.Value)
        ).ShouldBe(graph.Nodes.Length);
    }

    [Test]
    public async Task Subflow_RolledBackFrozenReferenceCannotStrandARevisionAndHostDeletionCascadesAllReferences()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var leaf = await Publish(service, Subflow(fixture.HostId));
        var parent = await Publish(service, Nesting(Subflow(fixture.HostId), leaf));
        var flowId = (
            await fixture.Flows.SaveAsync(Caller(fixture.HostId, parent), CancellationToken.None)
        )
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var closure = (
            await service.LoadClosureAsync(new(fixture.HostId), [parent.Id], CancellationToken.None)
        )
            .ShouldBeOfType<AutomationSubflowClosureOutcome.Available>()
            .Closure;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var run = new AutomationRunId(Guid.NewGuid());
            _ = db.AutomationFlowRuns.Add(
                new()
                {
                    Id = run.Value,
                    FlowId = flowId.Value,
                    HostId = fixture.HostId,
                    Status = AutomationFlowRunStatus.Waiting,
                }
            );
            (
                await AutomationSubflowRunReferences.AttachAsync(
                    db,
                    new(fixture.HostId),
                    run,
                    closure,
                    CancellationToken.None
                )
            ).ShouldBeEmpty();
            _ = await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
            _ = await db.Hosts.Where(host => host.Id == fixture.HostId).ExecuteDeleteAsync();
            (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(0);
            (await db.AutomationSubflowCallers.CountAsync()).ShouldBe(0);
            (await db.AutomationSubflowRevisionReferences.CountAsync()).ShouldBe(0);
        }
    }

    private static AutomationSubflowService Subflows(RuntimeFixture fixture) =>
        new(fixture.Database, fixture.Flows, fixture.Clock);

    private static async Task<AutomationSubflowRevision> Publish(
        AutomationSubflowService service,
        AutomationSubflowDraft draft
    ) =>
        (await service.PublishAsync(draft, CancellationToken.None))
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Published>()
            .Revision;

    private static AutomationPortMetadata SubflowPort(string id, AutomationPortValueType type) =>
        new(new(id), id, "Subflow data.", type);

    private static AutomationSubflowDraft Subflow(
        int hostId,
        AutomationSubflowInterface? contract = null
    )
    {
        contract ??= new([], []);
        var entry = Node(AutomationSubflowDefinitions.Entry, "{}") with
        {
            Definition = AutomationSubflowDefinitions.Boundary(
                AutomationSubflowDefinitions.Entry,
                contract
            ),
        };
        var exit = Node(AutomationSubflowDefinitions.Exit, "{}") with
        {
            Definition = AutomationSubflowDefinitions.Boundary(
                AutomationSubflowDefinitions.Exit,
                contract
            ),
            InputBindings = contract
                .Outputs.DistinctBy(port => port.Id)
                .ToImmutableDictionary(
                    port => new AutomationConfigurationFieldId(port.Id.Value),
                    _ => new AutomationInputBinding(AutomationInputBindingMode.Connected, null)
                ),
        };
        return new(
            new(Guid.NewGuid()),
            "Reusable graph",
            contract,
            Draft(
                hostId,
                [entry, exit],
                [
                    Edge(entry, "complete", exit),
                    .. contract.Outputs.Select(port =>
                        Edge(entry, port.Id.Value, exit, port.Id.Value, AutomationEdgeKind.Data)
                    ),
                ]
            ) with
            {
                IsEnabled = false,
            }
        );
    }

    private static AutomationFlowDraftNode Invoke(
        AutomationSubflowRevision revision,
        AutomationNodeId? nodeId = null,
        ImmutableDictionary<AutomationPortId, AutomationValue>? values = null
    ) =>
        Node(AutomationSubflowDefinitions.Invoke, "{}") with
        {
            Id = nodeId ?? new(Guid.NewGuid()),
            Definition = AutomationSubflowDefinitions.Invocation(revision, values),
            InputBindings = revision.Interface.Inputs.ToImmutableDictionary(
                port => new AutomationConfigurationFieldId(port.Id.Value),
                _ => new AutomationInputBinding(AutomationInputBindingMode.Fixed, null)
            ),
        };

    private static AutomationFlowDraft Caller(
        int hostId,
        AutomationSubflowRevision revision,
        ImmutableDictionary<AutomationPortId, AutomationValue>? values = null
    )
    {
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var invoke = Invoke(revision, values: values);
        return Draft(hostId, [source, invoke], [Edge(source, "flow", invoke)]);
    }

    private static AutomationSubflowDraft Nesting(
        AutomationSubflowDraft draft,
        AutomationSubflowRevision revision
    )
    {
        var invoke = Invoke(revision);
        return draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [draft.Graph.Nodes[0], invoke, draft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(draft.Graph.Nodes[0], "complete", invoke),
                    Edge(invoke, "complete", draft.Graph.Nodes[1]),
                ],
            },
        };
    }
}
