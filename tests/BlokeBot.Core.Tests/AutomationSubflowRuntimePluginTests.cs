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
    [Arguments(SubflowPluginChange.Disable)]
    [Arguments(SubflowPluginChange.Remove)]
    [Arguments(SubflowPluginChange.Update)]
    [Arguments(SubflowPluginChange.FeatureGeneration)]
    [Arguments(SubflowPluginChange.WorkerGeneration)]
    [Arguments(SubflowPluginChange.LifecycleOperation)]
    public async Task SubflowRuntime_PluginClosureFencesInvalidateParentBeforeAnyLaterCoreOrPluginEffect(
        SubflowPluginChange change
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var plugin = await SubflowPlugin(fixture);
        var leaf = Subflow(fixture.HostId);
        leaf = leaf with
        {
            Graph = leaf.Graph with
            {
                Nodes = [leaf.Graph.Nodes[0], plugin.Node, leaf.Graph.Nodes[1]],
                Edges =
                [
                    Edge(leaf.Graph.Nodes[0], "complete", plugin.Node),
                    Edge(plugin.Node, "complete", leaf.Graph.Nodes[1]),
                ],
            },
        };
        var service = new AutomationSubflowService(fixture.Database, plugin.Flows, fixture.Clock);
        var revision = await Publish(service, leaf);
        var nested = await Publish(service, Nesting(Subflow(fixture.HostId), revision));
        var caller = Caller(fixture.HostId, nested);
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var before = Node("send-chat", """{"message":"must-not-run"}""");
        caller = caller with
        {
            Nodes = [caller.Nodes[0], delay, before, caller.Nodes[1]],
            Edges =
            [
                Edge(caller.Nodes[0], "flow", delay),
                Edge(delay, "complete", before),
                Edge(before, "complete", caller.Nodes[1]),
            ],
        };
        _ = (
            await plugin.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var scenarios = new AutomationScenarioService(
            fixture.Database,
            plugin.Catalog,
            plugin.Flows,
            fixture.Clock
        );
        _ = (
            await scenarios.RunAsync(
                caller,
                scenarios.CreateDefaultFixture(caller, caller.Nodes[0].Id),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
        plugin.Invoker.Calls.ShouldBe(0);
        var dispatched = await plugin.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(2);
        }
        PluginFeatureRevision.TryCreate(2, out var nextRevision).ShouldBeTrue();
        PluginFeatureGeneration.TryCreate(2, out var nextGeneration).ShouldBeTrue();
        PluginWorkerGeneration.TryCreate(2, out var nextWorker).ShouldBeTrue();
        var changedState = plugin.State with { Revision = nextRevision };
        switch (change)
        {
            case SubflowPluginChange.Disable:
                plugin.Snapshots.Publish(
                    changedState with
                    {
                        Readiness = new PluginFeatureReadiness.Disabled(),
                    }
                );
                await new PluginAutomationRunCoordinator(
                    fixture.Database,
                    fixture.Clock
                ).CancelAsync(plugin.State, CancellationToken.None);
                break;
            case SubflowPluginChange.Remove:
                plugin.Declarations.Remove(plugin.Manifest.Manifest.Id, plugin.State.Fence);
                break;
            case SubflowPluginChange.Update:
                var updated = PluginManifestToml
                    .Validate(
                        PluginContractFixtures.ManifestReplacing(
                            "declaredVersion = \"1.2.0\"",
                            "declaredVersion = \"1.3.0\""
                        ),
                        PluginContractFixtures.CompatibleHost()
                    )
                    .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
                    .Manifest;
                plugin.Declarations.Publish(updated, plugin.State.Fence);
                break;
            case SubflowPluginChange.FeatureGeneration:
                plugin.Snapshots.Publish(changedState with { Generation = nextGeneration });
                break;
            case SubflowPluginChange.WorkerGeneration:
            case SubflowPluginChange.LifecycleOperation:
                var fence =
                    change == SubflowPluginChange.WorkerGeneration
                        ? new PluginLifecycleFence(plugin.State.Fence.OperationId, nextWorker)
                        : new PluginLifecycleFence(
                            PluginLifecycleOperationId.New(),
                            plugin.State.Fence.Generation
                        );
                plugin.Declarations.Publish(plugin.Manifest, fence);
                plugin.Snapshots.Publish(changedState with { Fence = fence });
                break;
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await plugin.Runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Invalidated
        );
        plugin.Invoker.Calls.ShouldBe(0);
        fixture.Chat.Calls.ShouldBe(0);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
            (
                await db.AutomationNodeRuns.AnyAsync(node =>
                    node.Status == AutomationNodeRunStatus.Pending
                    || node.Status == AutomationNodeRunStatus.Running
                    || node.Status == AutomationNodeRunStatus.Waiting
                )
            ).ShouldBeFalse();
        }
        PluginFeatureRevision.TryCreate(3, out var restoredRevision).ShouldBeTrue();
        plugin.Declarations.Publish(plugin.Manifest, plugin.State.Fence);
        plugin.Snapshots.Publish(plugin.State with { Revision = restoredRevision });
        var restarted = new AutomationRuntimeService(
            fixture.Database,
            plugin.Catalog,
            plugin.Flows,
            fixture.Actions,
            fixture.Clock
        );
        restarted.UsePluginExecution(plugin.Execution);
        (await restarted.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Invalidated
        );
        plugin.Invoker.Calls.ShouldBe(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SubflowRuntime_PluginReadinessUsesExistingIndependentVersusRequiredAdmission(
        bool required
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var plugin = await SubflowPlugin(fixture, required);
        var draft = Subflow(fixture.HostId);
        draft = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [draft.Graph.Nodes[0], plugin.Node, draft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(draft.Graph.Nodes[0], "complete", plugin.Node),
                    Edge(plugin.Node, "complete", draft.Graph.Nodes[1]),
                ],
            },
        };
        var revision = await Publish(new(fixture.Database, plugin.Flows, fixture.Clock), draft);
        var caller = Caller(fixture.HostId, revision);
        var before = Node("send-chat", """{"message":"admitted-only"}""");
        caller = caller with
        {
            Nodes = [caller.Nodes[0], before, caller.Nodes[1]],
            Edges =
            [
                Edge(caller.Nodes[0], "flow", before),
                Edge(before, "complete", caller.Nodes[1]),
            ],
        };
        _ = (
            await plugin.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        PluginFeatureRevision.TryCreate(2, out var nextRevision).ShouldBeTrue();
        PluginReadinessReason
            .TryCreate(
                PluginReadinessReasonCode.MissingScopes,
                PluginRecoveryAction.ReconnectTwitch,
                "fixture",
                out var reason
            )
            .ShouldBeTrue();
        plugin.Snapshots.Publish(
            plugin.State with
            {
                Revision = nextRevision,
                Readiness = new PluginFeatureReadiness.EnabledDegraded(reason),
            }
        );
        var result = await plugin.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        result.Status.ShouldBe(
            required ? AutomationDispatchStatus.InvalidFlow : AutomationDispatchStatus.Accepted
        );
        plugin.Invoker.Calls.ShouldBe(required ? 0 : 1);
        fixture.Chat.Calls.ShouldBe(required ? 0 : 1);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(required ? 0 : 1);
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
    }

    public enum SubflowPluginChange
    {
        Disable,
        Remove,
        Update,
        FeatureGeneration,
        WorkerGeneration,
        LifecycleOperation,
    }

    private static async Task<SubflowPluginServices> SubflowPlugin(
        RuntimeFixture fixture,
        bool required = false
    )
    {
        var manifest = PluginManifestToml
            .Validate(
                required
                    ? PluginContractFixtures.ManifestReplacing(
                        "scopes = []",
                        "scopes = [\"moderator:read:chatters\"]"
                    )
                    : PluginContractFixtures.CompleteManifestToml(),
                PluginContractFixtures.CompatibleHost()
            )
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest;
        PluginHostId.TryCreate(fixture.HostId, out var host).ShouldBeTrue();
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
        var definitions = new AutomationDefinitionCatalog(
            [new CoreAutomationCatalogModule()],
            registry
        );
        var invoker = new SubflowRecordingInvoker();
        var execution = new PluginAutomationExecutionService(definitions, invoker);
        var catalog = new AutomationCatalogService(
            definitions,
            fixture.Features,
            fixture.Expressions,
            pluginExecution: execution
        );
        var flows = new AutomationFlowService(
            fixture.Database,
            catalog,
            fixture.Expressions,
            new NoOverlayCues(),
            fixture.Clock
        );
        var runtime = new AutomationRuntimeService(
            fixture.Database,
            catalog,
            flows,
            fixture.Actions,
            fixture.Clock
        );
        runtime.UsePluginExecution(execution);
        var descriptor = (
            await catalog.DiscoverAsync(new(fixture.HostId), CancellationToken.None)
        ).Definitions.Single(value =>
            value.Id.Value.EndsWith(".publish-link", StringComparison.Ordinal)
        );
        var node = Node(
            descriptor.Id.Value,
            """{"link":"https://example.invalid"}""",
            bindings: Bindings("link", AutomationInputBindingMode.Fixed)
        );
        node = node with
        {
            Definition = node.Definition with { PluginProvenance = descriptor.PluginProvenance },
        };
        return new(
            catalog,
            flows,
            runtime,
            execution,
            invoker,
            state,
            manifest,
            declarations,
            snapshots,
            node
        );
    }

    private sealed record SubflowPluginServices(
        AutomationCatalogService Catalog,
        AutomationFlowService Flows,
        AutomationRuntimeService Runtime,
        PluginAutomationExecutionService Execution,
        SubflowRecordingInvoker Invoker,
        PluginFeatureState State,
        ValidatedPluginManifest Manifest,
        PluginFeatureDeclarationRegistry Declarations,
        PluginFeatureSnapshotRegistry Snapshots,
        AutomationFlowDraftNode Node
    );

    private sealed class SubflowRecordingInvoker : IPluginAutomationInvoker
    {
        internal int Calls { get; private set; }

        public ValueTask<PluginDispatchInvocationOutcome> InvokeAutomationAsync(
            PluginAutomationEndpoint endpoint,
            PluginInvocationContext.Automation context,
            PluginValue input,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return ValueTask.FromResult<PluginDispatchInvocationOutcome>(
                new PluginDispatchInvocationOutcome.Returned(new PluginValue.Nil())
            );
        }
    }
}
