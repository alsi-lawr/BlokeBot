using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task Authoring_PreviewReportsCallersWithoutWritingAndPublicationRevalidates()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var draft = Subflow(fixture.HostId);
        var original = await Publish(service, draft);
        var callerDraft = Caller(fixture.HostId, original) with { Name = "Live greetings" };
        var caller = (
            await fixture.Flows.SaveAsync(callerDraft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var contract = new AutomationSubflowInterface(
            [SubflowPort("message", AutomationPortValueType.Text)],
            [SubflowPort("message", AutomationPortValueType.Text)]
        );
        var changed = Subflow(fixture.HostId, contract) with { Id = draft.Id };
        var preview = (
            await service.PreviewAsync(changed, CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPreviewOutcome.Ready>();
        preview.IncompatibleCallers.ShouldHaveSingleItem().FlowId.ShouldBe(caller.FlowId);
        var details = await service.DescribeCallersAsync(
            new(fixture.HostId),
            preview.IncompatibleCallers,
            CancellationToken.None
        );
        details.ShouldHaveSingleItem().Name.ShouldBe(callerDraft.Name);
        (
            await service.DescribeCallersAsync(
                new(fixture.HostId + 1),
                preview.IncompatibleCallers,
                CancellationToken.None
            )
        ).ShouldBeEmpty();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(1);
            (await db.AutomationSubflows.Select(row => row.LastRevision).SingleAsync()).ShouldBe(1);
            (await db.AutomationSubflowCallers.Select(row => row.SubflowId).SingleAsync()).ShouldBe(
                original.SubflowId.Value
            );
        }
        _ = await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        _ = (
            await service.PublishAsync(changed, CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>();
        await using var unchanged = await fixture.Database.CreateDbContextAsync();
        (await unchanged.AutomationSubflowRevisions.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task Authoring_InvocationRoundTripAndHistoryPreserveCurrentInterfaceAndStructuredFixedValues()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var ports = ImmutableArray.Create(
            SubflowPort("message", AutomationPortValueType.Text),
            SubflowPort("payload", AutomationPortValueType.Map)
        );
        var published = await Publish(
            Subflows(fixture),
            Subflow(fixture.HostId, new(ports, ports))
        );
        var values = ImmutableDictionary<AutomationPortId, AutomationValue>
            .Empty.Add(new("message"), new AutomationValue.Text("original"))
            .Add(
                new("payload"),
                new AutomationValue.Map([
                    new(
                        "nested",
                        new AutomationValue.Array([
                            new AutomationValue.Number(17),
                            new AutomationValue.Text("kept"),
                        ])
                    ),
                ])
            );
        var draft = Caller(fixture.HostId, published, values);
        var editor = AuthoringEditor(draft, fixture.Catalog);
        var original = editor.Draft(new(fixture.HostId));
        JsonElement
            .DeepEquals(
                original.Nodes[1].Definition.Configuration,
                draft.Nodes[1].Definition.Configuration
            )
            .ShouldBeTrue();
        var history = new AutomationEditorHistory();
        history.StartLoaded(editor);
        editor.Nodes[1].SetValue(new("message"), "edited");
        history.Record(editor).ShouldBeTrue();
        var changed = editor.Draft(new(fixture.HostId));
        AutomationSubflowDefinitions
            .TryRead(changed.Nodes[1].Definition, out var read)
            .ShouldBeTrue();
        read.ShouldBeOfType<AutomationSubflowInvocationConfiguration>()
            .SubflowId.ShouldBe(published.SubflowId);
        AutomationEditorNode
            .DisplayFixedValue(read.FixedInputs[new("payload")].Value)
            .ShouldBe(AutomationEditorNode.DisplayFixedValue(values[new("payload")]));
        editor = history.Undo(editor).ShouldNotBeNull();
        JsonElement
            .DeepEquals(
                editor.Draft(new(fixture.HostId)).Nodes[1].Definition.Configuration,
                original.Nodes[1].Definition.Configuration
            )
            .ShouldBeTrue();
        editor = history.Redo(editor).ShouldNotBeNull();
        JsonElement
            .DeepEquals(
                editor.Draft(new(fixture.HostId)).Nodes[1].Definition.Configuration,
                changed.Nodes[1].Definition.Configuration
            )
            .ShouldBeTrue();
        var nextRevision = await Publish(
            Subflows(fixture),
            Subflow(
                fixture.HostId,
                new(ports.Add(SubflowPort("extra", AutomationPortValueType.Text)), ports)
            ) with
            {
                Id = published.SubflowId,
            }
        );
        editor
            .Nodes[1]
            .ReplaceSubflowDefinition(
                AutomationSubflowDefinitions.Invocation(
                    nextRevision,
                    read.FixedInputs.ToImmutableDictionary(
                        pair => pair.Key,
                        pair => pair.Value.Value
                    )
                ),
                fixture.Catalog
            );
        history.Record(editor).ShouldBeTrue();
        editor
            .Nodes[1]
            .Subflow.ShouldBeOfType<AutomationSubflowInvocationConfiguration>()
            .SubflowId.ShouldBe(nextRevision.SubflowId);
        editor.Nodes[1].Subflow!.Interface.Inputs.Length.ShouldBe(3);
        editor = history.Undo(editor).ShouldNotBeNull();
        editor.Nodes[1].Subflow!.Interface.Inputs.Length.ShouldBe(2);
        editor = history.Redo(editor).ShouldNotBeNull();
        editor.Nodes[1].Subflow!.Interface.Inputs.Length.ShouldBe(3);
    }

    [Test]
    public async Task Authoring_ExtractionPreservesControlAndDataThenUndoesAsOneDraftEdit()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var draft = AuthoringExtractionDraft(fixture.HostId);
        var editor = AuthoringEditor(draft, fixture.Catalog);
        var original = AutomationEditorDraftSnapshot.Capture(editor);
        var history = new AutomationEditorHistory();
        history.StartLoaded(editor);
        var preview = AutomationExtraction.Preview(
            editor,
            new(fixture.HostId),
            [draft.Nodes[1].Id, draft.Nodes[2].Id],
            "Delayed greeting",
            new(Guid.NewGuid()),
            fixture.Catalog
        );
        preview.Errors.ShouldBeEmpty();
        preview.Inputs.ShouldHaveSingleItem().Port.Id.Value.ShouldBe("message");
        preview.Outputs.ShouldBeEmpty();
        var service = Subflows(fixture);
        _ = (
            await service.PreviewAsync(preview.Subflow!, CancellationToken.None)
        ).ShouldBeOfType<AutomationSubflowPreviewOutcome.Ready>();
        var published = await Publish(service, preview.Subflow!);
        editor = AutomationExtraction.Replace(editor, preview, published, fixture.Catalog);
        history.Record(editor).ShouldBeTrue();
        history.UndoCount.ShouldBe(1);
        var replaced = AutomationEditorDraftSnapshot.Capture(editor);
        var outcome = await fixture.Scenarios.RunDefaultAsync(
            editor.Draft(new(fixture.HostId)),
            draft.Nodes[0].Id,
            CancellationToken.None
        );
        _ = outcome.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        fixture.Chat.Messages.ShouldBeEmpty();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (await db.AutomationFlows.CountAsync()).ShouldBe(0);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        }
        editor = history.Undo(editor).ShouldNotBeNull();
        original.Matches(editor).ShouldBeTrue();
        editor = history.Redo(editor).ShouldNotBeNull();
        replaced.Matches(editor).ShouldBeTrue();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Authoring_ExtractionPreservesSharedProducerAndFailureOrdering(
        bool failSecond,
        bool continueSecond
    )
    {
        foreach (var extract in new[] { false, true })
        {
            await using var fixture = await RuntimeFixture.CreateAsync(
                chatAdmissions: [true, !failSecond, true]
            );
            var draft = AuthoringExtractionDraft(fixture.HostId);
            var second = draft.Nodes[1] with
            {
                Id = new(Guid.NewGuid()),
                FailurePolicy = continueSecond
                    ? AutomationNodeFailurePolicy.Continue
                    : AutomationNodeFailurePolicy.Stop,
            };
            var after = Node("send-chat", """{"message":"after"}""");
            draft = draft with
            {
                Nodes = draft.Nodes.Add(second).Add(after),
                Edges = draft
                    .Edges.Add(Edge(draft.Nodes[2], "complete", second))
                    .Add(
                        Edge(draft.Nodes[3], "message", second, "message", AutomationEdgeKind.Data)
                    )
                    .Add(Edge(second, "complete", after)),
                IsEnabled = true,
            };
            (
                await fixture.Flows.ValidateAsync(draft, CancellationToken.None)
            ).Errors.ShouldBeEmpty();
            var editor = AuthoringEditor(draft, fixture.Catalog);
            var original = AutomationEditorDraftSnapshot.Capture(editor);
            var history = new AutomationEditorHistory();
            history.StartLoaded(editor);
            if (extract)
            {
                var preview = AutomationExtraction.Preview(
                    editor,
                    new(fixture.HostId),
                    [draft.Nodes[1].Id, draft.Nodes[2].Id, second.Id],
                    "Shared greeting",
                    new(Guid.NewGuid()),
                    fixture.Catalog
                );
                preview.Errors.ShouldBeEmpty();
                _ = preview.Inputs.ShouldHaveSingleItem();
                var prepared = (
                    await Subflows(fixture).PreviewAsync(preview.Subflow!, CancellationToken.None)
                ).ShouldBeOfType<AutomationSubflowPreviewOutcome.Ready>();
                var prospective = AutomationExtraction.Replace(
                    editor,
                    preview,
                    prepared.Candidate,
                    fixture.Catalog
                );
                (
                    await fixture.Flows.ValidateExtractionReplacementAsync(
                        prospective.Draft(new(fixture.HostId)),
                        null,
                        null,
                        prepared.Candidate,
                        CancellationToken.None
                    )
                ).Errors.ShouldBeEmpty();
                await using (var db = await fixture.Database.CreateDbContextAsync())
                {
                    (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(0);
                }
                var published = await Publish(Subflows(fixture), preview.Subflow!);
                editor = AutomationExtraction.Replace(editor, preview, published, fixture.Catalog);
                history.Record(editor).ShouldBeTrue();
                var caller = editor.Nodes.Single(node =>
                    node.Definition.Id.Value == AutomationSubflowDefinitions.Invoke
                );
                editor
                    .Edges.Count(edge =>
                        edge.TargetNodeId == caller.Id && edge.Kind == AutomationEdgeKind.Data
                    )
                    .ShouldBe(1);
                draft = editor.Draft(new(fixture.HostId));
                (
                    await fixture.Flows.ValidateAsync(draft, CancellationToken.None)
                ).Errors.ShouldBeEmpty();
                original.Matches(history.Undo(editor).ShouldNotBeNull()).ShouldBeTrue();
            }
            _ = (
                await fixture.Flows.SaveAsync(draft, CancellationToken.None)
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
            var dispatch = await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            );
            fixture.Clock.Advance(TimeSpan.FromSeconds(3));
            var result = await fixture.Runtime.ResumeAsync(
                dispatch.RunIds.ShouldHaveSingleItem(),
                CancellationToken.None
            );
            result.Status.ShouldBe(
                failSecond && !continueSecond
                    ? AutomationResumeStatus.Failed
                    : AutomationResumeStatus.Completed
            );
            fixture.Chat.Messages.ShouldBe(
                failSecond && !continueSecond ? ["hello", "hello"] : ["hello", "hello", "after"]
            );
        }
    }

    [Test]
    public async Task Authoring_ExtractionPreservesInputResolutionFailureBeforeDelay()
    {
        foreach (var extract in new[] { false, true })
        {
            await using var fixture = await RuntimeFixture.CreateAsync();
            var draft = AuthoringExtractionDraft(fixture.HostId);
            var before = Node("send-chat", """{"message":"before"}""");
            var broken = Node(
                "cel-transform",
                """{"inputs":[],"outputs":[{"port-id":"message","display-name":"Message","type":"Text","nullability":"NonNullable","cel":"string(1 / 0)"}]}"""
            ) with
            {
                Id = draft.Nodes[3].Id,
            };
            draft = draft with
            {
                Nodes = draft.Nodes.SetItem(3, broken).Add(before),
                Edges =
                [
                    Edge(draft.Nodes[0], "flow", before),
                    Edge(before, "complete", draft.Nodes[1]),
                    draft.Edges[1],
                    draft.Edges[2],
                ],
                IsEnabled = true,
            };
            (
                await fixture.Flows.ValidateAsync(draft, CancellationToken.None)
            ).Errors.ShouldBeEmpty();
            if (extract)
            {
                var editor = AuthoringEditor(draft, fixture.Catalog);
                var preview = AutomationExtraction.Preview(
                    editor,
                    new(fixture.HostId),
                    [draft.Nodes[1].Id, draft.Nodes[2].Id],
                    "Greeting",
                    new(Guid.NewGuid()),
                    fixture.Catalog
                );
                preview.Errors.ShouldBeEmpty();
                var revision = await Publish(Subflows(fixture), preview.Subflow!);
                draft = AutomationExtraction
                    .Replace(editor, preview, revision, fixture.Catalog)
                    .Draft(new(fixture.HostId));
            }
            _ = (
                await fixture.Flows.SaveAsync(draft, CancellationToken.None)
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
            var dispatch = await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            );
            var run = dispatch.RunIds.ShouldHaveSingleItem();
            (await fixture.Runtime.ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
                AutomationResumeStatus.Failed
            );
            fixture.Chat.Messages.ShouldBe(["before"]);
            (await ReadTraceAsync(fixture, new(run.Value))).Events.ShouldNotContain(item =>
                item.Event.Kind == AutomationTraceEventKind.Delay
            );
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Authoring_ExtractionRejectsHoistingAcrossPriorEffectOrContinue(
        bool firstContinues
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var draft = AuthoringExtractionDraft(fixture.HostId);
        draft = firstContinues
            ? draft with
            {
                Nodes = draft.Nodes.SetItem(
                    1,
                    draft.Nodes[1] with
                    {
                        FailurePolicy = AutomationNodeFailurePolicy.Continue,
                    }
                ),
            }
            : draft with
            {
                Edges =
                [
                    Edge(draft.Nodes[0], "flow", draft.Nodes[2]),
                    Edge(draft.Nodes[2], "complete", draft.Nodes[1]),
                    draft.Edges[2],
                ],
            };
        var preview = AutomationExtraction.Preview(
            AuthoringEditor(draft, fixture.Catalog),
            new(fixture.HostId),
            [draft.Nodes[1].Id, draft.Nodes[2].Id],
            "Greeting",
            new(Guid.NewGuid()),
            fixture.Catalog
        );
        preview.Errors.ShouldContain(error => error.Code == "extraction-input-order");
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Authoring_ExtractionRejectsDistinctControlEntriesWithoutChangingSelection()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var draft = AuthoringExtractionDraft(fixture.HostId);
        draft = draft with
        {
            Edges = draft.Edges.Add(Edge(draft.Nodes[0], "flow", draft.Nodes[2])),
        };
        var editor = AuthoringEditor(draft, fixture.Catalog);
        var before = AutomationEditorDraftSnapshot.Capture(editor);
        var preview = AutomationExtraction.Preview(
            editor,
            new(fixture.HostId),
            [draft.Nodes[1].Id, draft.Nodes[2].Id],
            "Delayed greeting",
            new(Guid.NewGuid()),
            fixture.Catalog
        );
        preview.IsValid.ShouldBeFalse();
        preview.Incoming.Count(edge => edge.Kind == AutomationEdgeKind.Flow).ShouldBe(2);
        preview.Errors.ShouldContain(error => error.Code == "extraction-entry");
        before.Matches(editor).ShouldBeTrue();
    }

    [Test]
    public async Task Authoring_ActualNestedTraceOccurrencesMapToTheirOwnRootInvocation()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var leafDraft = Subflow(fixture.HostId);
        var action = Node("send-chat", """{"message":"inner"}""");
        leafDraft = leafDraft with
        {
            Graph = leafDraft.Graph with
            {
                Nodes = [leafDraft.Graph.Nodes[0], action, leafDraft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(leafDraft.Graph.Nodes[0], "complete", action),
                    Edge(action, "complete", leafDraft.Graph.Nodes[1]),
                ],
            },
        };
        var leaf = await Publish(service, leafDraft);
        var outer = await Publish(service, Nesting(Subflow(fixture.HostId), leaf));
        var draft = Caller(fixture.HostId, outer);
        var second = Invoke(outer);
        draft = draft with
        {
            Nodes = draft.Nodes.Add(second),
            Edges = draft.Edges.Add(Edge(draft.Nodes[1], "complete", second)),
        };
        var scenario = (
            await fixture.Scenarios.RunDefaultAsync(
                draft,
                draft.Nodes[0].Id,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        var trace = await ReadTraceAsync(fixture, scenario.TraceId);
        var presentation = AutomationTracePresentation.Create(trace);
        var nested = presentation
            .Rows.Where(row =>
                row.Entry.Event.Kind == AutomationTraceEventKind.Attempt
                && row.Entry.Event.Node?.Id == action.Id
            )
            .ToArray();
        nested.Length.ShouldBe(2);
        nested.Select(row => row.RootNodeId).ShouldBe([draft.Nodes[1].Id, second.Id]);
        nested.Select(row => row.Context).Distinct().Count().ShouldBe(2);
        presentation.Find(nested[1].Entry.Sequence).ShouldBeSameAs(nested[1]);
        presentation
            .Rows.Where(row => row.Entry.Event.Invocation.ParentId is not null)
            .ShouldAllBe(row => row.RootNodeId != null);
        var saved = (
            await fixture.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var dispatch = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var live = await ReadTraceAsync(fixture, new(dispatch.RunIds.Single().Value));
        AutomationTracePresentation
            .Create(live)
            .Rows.Where(row =>
                row.Entry.Event.Kind == AutomationTraceEventKind.Attempt
                && row.Entry.Event.Node?.Id == action.Id
            )
            .Select(row => row.RootNodeId)
            .ShouldBe([draft.Nodes[1].Id, second.Id]);
        saved.FlowId.Value.ShouldNotBe(Guid.Empty);
    }

    private static AutomationEditorState AuthoringEditor(
        AutomationFlowDraft draft,
        AutomationCatalogService catalog
    ) =>
        AutomationEditorState.Restore(
            draft,
            draft.Nodes.ToDictionary(
                node => node.Id,
                node =>
                    (
                        (AutomationConfigurationCheck.Valid)
                            catalog.ValidatePersistedDefinition(node.Definition)
                    ).Definition
            )
        );

    private static AutomationFlowDraft AuthoringExtractionDraft(int hostId)
    {
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":2000}""");
        var action = Node("send-chat", """{"message":"fallback"}""") with
        {
            InputBindings = ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationInputBinding
            >.Empty.Add(new("message"), new(AutomationInputBindingMode.Connected, null)),
        };
        var value = Node(
            "cel-transform",
            """{"inputs":[],"outputs":[{"port-id":"message","display-name":"Message","type":"Text","nullability":"NonNullable","cel":"\"hello\""}]}"""
        );
        return Draft(
            hostId,
            [source, action, delay, value],
            [
                Edge(source, "flow", action),
                Edge(action, "complete", delay),
                Edge(value, "message", action, "message", AutomationEdgeKind.Data),
            ]
        ) with
        {
            IsEnabled = false,
        };
    }
}
