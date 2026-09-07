using System.Collections.Immutable;
using System.Data.Common;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task SubflowLatest_PublicationChangesFutureCallsAndDraftScenariosButNotAdmittedWork()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var library = Subflows(fixture);
        var draft = Subflow(fixture.HostId);
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var send = Node("send-chat", """{"message":"original"}""");
        draft = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [draft.Graph.Nodes[0], delay, send, draft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(draft.Graph.Nodes[0], "complete", delay),
                    Edge(delay, "complete", send),
                    Edge(send, "complete", draft.Graph.Nodes[1]),
                ],
            },
        };
        var original = await Publish(library, draft);
        var caller = Caller(fixture.HostId, original);
        var saved = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var admitted = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var oldRun = admitted.RunIds.ShouldHaveSingleItem();
        string definition;
        string authored;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            definition = (await db.AutomationFlowRuns.SingleAsync()).DefinitionJson;
            authored = (
                await db.AutomationFlowNodes.SingleAsync(node =>
                    node.Id == caller.Nodes[1].Id.Value
                )
            ).ConfigurationJson;
        }
        var currentSend = send with
        {
            Definition = Node("send-chat", """{"message":"current"}""").Definition,
        };
        var current = await Publish(
            library,
            draft with
            {
                Graph = draft.Graph with
                {
                    Nodes = [draft.Graph.Nodes[0], currentSend, draft.Graph.Nodes[^1]],
                    Edges =
                    [
                        Edge(draft.Graph.Nodes[0], "complete", currentSend),
                        Edge(currentSend, "complete", draft.Graph.Nodes[^1]),
                    ],
                },
            }
        );
        var next = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        next.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        fixture.Chat.Messages.ShouldBe(["current"]);
        var scenario = (
            await fixture.Scenarios.RunAsync(
                caller,
                fixture.Scenarios.CreateDefaultFixture(caller, caller.Nodes[0].Id),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        var trace = await ReadTraceAsync(fixture, scenario.TraceId);
        trace.Events.ShouldContain(item => item.Event.Invocation.RevisionId == current.Id.Value);
        trace.Events.ShouldNotContain(item =>
            item.Event.Invocation.RevisionId == original.Id.Value
        );
        fixture.Chat.Messages.ShouldBe(["current"]);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await fixture.NewRuntime().ResumeAsync(oldRun, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        fixture.Chat.Messages.ShouldBe(["current", "original"]);
        (await fixture.NewRuntime().ResumeAsync(oldRun, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        fixture.Chat.Messages.ShouldBe(["current", "original"]);
        await using var unchanged = await fixture.Database.CreateDbContextAsync();
        (
            await unchanged.AutomationFlowRuns.SingleAsync(run => run.Id == oldRun.Value)
        ).DefinitionJson.ShouldBe(definition);
        (
            await unchanged.AutomationFlowNodes.SingleAsync(node =>
                node.Id == caller.Nodes[1].Id.Value
            )
        ).ConfigurationJson.ShouldBe(authored);
        (
            await unchanged.AutomationFlows.SingleAsync(flow => flow.Id == saved.FlowId.Value)
        ).Name.ShouldBe(caller.Name);
    }

    [Test]
    public async Task SubflowLatest_IncompatibleOrdinaryAndCurrentNestedCallsBlockBeforeAdmissionAndTrace()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var library = Subflows(fixture);
        var leafDraft = Subflow(fixture.HostId);
        var leaf = await Publish(library, leafDraft);
        var nestedDraft = Nesting(Subflow(fixture.HostId), leaf);
        _ = await Publish(library, nestedDraft);
        var nested = await Publish(
            library,
            nestedDraft with
            {
                Description = "Current nested caller",
            }
        );
        var direct = Caller(fixture.HostId, leaf);
        var directSaved = (
            await fixture.Flows.SaveAsync(direct, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var indirect = Caller(fixture.HostId, nested);
        _ = (
            await fixture.Flows.SaveAsync(indirect, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var ports = ImmutableArray.Create(SubflowPort("text", AutomationPortValueType.Text));
        var changed = Subflow(fixture.HostId, new(ports, ports)) with { Id = leaf.SubflowId };
        var preview = (
            await library.PreviewAsync(changed, CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPreviewOutcome.Ready>();
        preview.IncompatibleCallers.Length.ShouldBe(2);
        preview.IncompatibleCallers.ShouldContain(call => call.FlowId == directSaved.FlowId);
        preview.IncompatibleCallers.ShouldContain(call => call.SubflowId == nested.SubflowId);
        _ = await Publish(library, changed);
        var rejected = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        rejected.Status.ShouldBe(AutomationDispatchStatus.InvalidFlow);
        (
            await fixture.Scenarios.RunAsync(
                indirect,
                fixture.Scenarios.CreateDefaultFixture(indirect, indirect.Nodes[0].Id),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "subflow-interface-incompatible");
        fixture.Chat.Calls.ShouldBe(0);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationTraces.CountAsync()).ShouldBe(0);
        (await db.AutomationTraceEvents.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task SubflowLatest_OneResolutionCutSerializesPublicationAcrossRepeatedNestedCalls()
    {
        var window = new CurrentResolutionWindow();
        await using var fixture = await RuntimeFixture.CreateAsync(databaseInterceptors: [window]);
        var library = Subflows(fixture);
        var leafDraft = Subflow(fixture.HostId);
        var send = Node("send-chat", """{"message":"before"}""");
        leafDraft = leafDraft with
        {
            Graph = leafDraft.Graph with
            {
                Nodes = [leafDraft.Graph.Nodes[0], send, leafDraft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(leafDraft.Graph.Nodes[0], "complete", send),
                    Edge(send, "complete", leafDraft.Graph.Nodes[1]),
                ],
            },
        };
        var leaf = await Publish(library, leafDraft);
        var outer = await Publish(library, Nesting(Subflow(fixture.HostId), leaf));
        var caller = Caller(fixture.HostId, outer);
        var repeat = Invoke(outer, new(Guid.NewGuid()));
        caller = caller with
        {
            Nodes = [.. caller.Nodes, repeat],
            Edges = [.. caller.Edges, Edge(caller.Nodes[1], "complete", repeat)],
        };
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        window.Armed = true;
        var operation = fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        await window.Paused.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var changedSend = send with
        {
            Definition = Node("send-chat", """{"message":"after"}""").Definition,
        };
        var publication = Task.Run(() =>
            Publish(
                library,
                leafDraft with
                {
                    Graph = leafDraft.Graph with
                    {
                        Nodes = [leafDraft.Graph.Nodes[0], changedSend, leafDraft.Graph.Nodes[^1]],
                    },
                }
            )
        );
        try
        {
            await window.PublicationRead.Task.WaitAsync(TimeSpan.FromSeconds(20));
            publication.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            _ = window.Release.TrySetResult();
        }
        (await operation.WaitAsync(TimeSpan.FromSeconds(30))).Status.ShouldBe(
            AutomationDispatchStatus.Accepted
        );
        _ = await publication.WaitAsync(TimeSpan.FromSeconds(30));
        window.Armed = false;
        window.CurrentReads.ShouldBe(2);
        fixture.Chat.Messages.ShouldBe(["before", "before"]);
        (
            await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationDispatchStatus.Accepted);
        fixture.Chat.Messages.ShouldBe(["before", "before", "after", "after"]);
    }

    [Test]
    public async Task SubflowLatest_FeatureRequirementsComeFromResolvedGraphNotOldTransitiveMetadata()
    {
        var overlays = new HostBoundOverlayCues();
        await using var fixture = await RuntimeFixture.CreateAsync(
            overlays: overlays,
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Overlays
        );
        var target = Guid.NewGuid();
        var cue = Guid.NewGuid();
        overlays.AddTarget(fixture.HostId, target, OverlayType.CuePlayer);
        overlays.AddCue(fixture.HostId, cue, OverlayCueQueuePolicy.Replace);
        var draft = Subflow(fixture.HostId);
        var action = Node(
            "play-overlay-cue",
            $$"""{"target-id":"{{target}}","cue-id":"{{cue}}"}"""
        );
        draft = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [draft.Graph.Nodes[0], action, draft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(draft.Graph.Nodes[0], "complete", action),
                    Edge(action, "complete", draft.Graph.Nodes[1]),
                ],
            },
        };
        var library = Subflows(fixture);
        var leaf = await Publish(library, draft);
        var parent = await Publish(library, Nesting(Subflow(fixture.HostId), leaf));
        _ = (
            await fixture.Flows.SaveAsync(Caller(fixture.HostId, parent), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var send = Node("send-chat", """{"message":"current-without-overlay"}""") with
        {
            Id = action.Id,
        };
        _ = await Publish(
            library,
            draft with
            {
                Graph = draft.Graph with
                {
                    Nodes = [draft.Graph.Nodes[0], send, draft.Graph.Nodes[^1]],
                },
            }
        );
        _ = await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Overlays,
            CancellationToken.None
        );
        (
            await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationDispatchStatus.Accepted);
        fixture.Chat.Messages.ShouldBe(["current-without-overlay"]);
    }

    private sealed class CurrentResolutionWindow : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal int CurrentReads { get; private set; }
        private Guid? _resolutionContext;
        internal TaskCompletionSource Paused { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource PublicationRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Armed)
            {
                return result;
            }
            if (
                command.CommandText.Contains("FROM \"hosts\"", StringComparison.Ordinal)
                && Paused.Task.IsCompleted
                && !Release.Task.IsCompleted
            )
            {
                _ = PublicationRead.TrySetResult();
            }
            if (
                command.CommandText.Contains("\"automation_subflows\"", StringComparison.Ordinal)
                && command.CommandText.Contains(
                    "\"automation_subflow_revisions\"",
                    StringComparison.Ordinal
                )
                && command.CommandText.Contains("LastRevision", StringComparison.Ordinal)
            )
            {
                _resolutionContext ??= eventData.Context!.ContextId.InstanceId;
                if (_resolutionContext != eventData.Context!.ContextId.InstanceId)
                {
                    return result;
                }
                CurrentReads++;
                if (CurrentReads == 1)
                {
                    _ = Paused.TrySetResult();
                    await Release.Task.WaitAsync(cancellationToken);
                }
            }
            return result;
        }
    }
}
