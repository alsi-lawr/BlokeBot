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
    public async Task SendChat_UndefinedDraftModeIsInvalidAndCannotOverwriteStoredFlow()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var trigger = Node("custom-command", """{"custom-command-id":7}""");
        var send = Node("send-chat", """{"message":"Retained fixed message"}""");
        var id = await fixture.SaveAsync([trigger, send], [Edge(trigger, "flow", send)]);
        var original = (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.ShouldHaveSingleItem();
        string originalConfiguration;
        string originalBindings;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var row = await db.AutomationFlowNodes.SingleAsync(node => node.Id == send.Id.Value);
            originalConfiguration = row.ConfigurationJson;
            originalBindings = row.InputBindingsJson;
        }
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var candidate = original.Draft with
        {
            Name = "Must not overwrite the stored flow",
            IsEnabled = false,
            Nodes =
            [
                trigger,
                send with
                {
                    Definition = Persisted("send-chat", """{"message":"Must not be saved"}"""),
                    InputBindings = Bindings("message", (AutomationInputBindingMode)99),
                },
            ],
        };
        var validation = (
            await fixture.Flows.ValidateDraftAsync(candidate, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Invalid>();
        validation.Errors.ShouldContain(error =>
            error.NodeId == send.Id
            && error.FieldId == new AutomationConfigurationFieldId("message")
        );
        var save = (
            await fixture.Flows.SaveAsync(candidate, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        save.Errors.ShouldContain(error =>
            error.NodeId == send.Id
            && error.FieldId == new AutomationConfigurationFieldId("message")
        );
        var retained = (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.ShouldHaveSingleItem();
        retained.Draft.Id.ShouldBe(id);
        retained.Draft.Name.ShouldBe(original.Draft.Name);
        retained.Draft.IsEnabled.ShouldBe(original.Draft.IsEnabled);
        retained.UpdatedAtUtc.ShouldBe(original.UpdatedAtUtc);
        retained.Draft.Edges.ShouldBe(original.Draft.Edges);
        await using var verify = await fixture.Database.CreateDbContextAsync();
        var retainedSend = await verify.AutomationFlowNodes.SingleAsync(node =>
            node.Id == send.Id.Value
        );
        retainedSend.ConfigurationJson.ShouldBe(originalConfiguration);
        retainedSend.InputBindingsJson.ShouldBe(originalBindings);
    }

    [Test]
    public async Task SendChat_UnsupportedStoredExpressionRemainsInspectableAndRequiresExplicitRepair()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var trigger = Node("custom-command", """{"custom-command-id":7}""");
        var send = Node("send-chat", """{"message":"Retained fixed\nmessage"}""");
        var id = await fixture.SaveAsync([trigger, send], [Edge(trigger, "flow", send)]);
        var unsupportedBindings = AutomationRuntimeSerialization.SerializeInputBindings(
            Bindings(
                "message",
                AutomationInputBindingMode.Expression,
                new(new(1), "actor.display_name")
            )
        );
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var row = await db.AutomationFlowNodes.SingleAsync(node => node.Id == send.Id.Value);
            row.InputBindingsJson = unsupportedBindings;
            _ = await db.SaveChangesAsync();
        }
        var read = (
            await fixture.Flows.ReadForAuthoringAsync(
                new(fixture.HostId),
                id,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Editor>();
        read.Errors.ShouldContain(error =>
            error.NodeId == send.Id
            && error.FieldId == new AutomationConfigurationFieldId("message")
        );
        var descriptors = read.Snapshot.Draft.Nodes.ToDictionary(
            node => node.Id,
            node =>
                fixture
                    .Catalog.DescribeForAuthoring(new(fixture.HostId), node)
                    .ShouldBeOfType<AutomationConfigurationCheck.Valid>()
                    .Definition
        );
        var editor = AutomationEditorState.Restore(read.Snapshot, descriptors);
        editor.Nodes.ShouldAllBe(node => node.ProjectionPreservesOriginal);
        _ = (
            await fixture.Flows.SaveAsync(editor.Draft(new(fixture.HostId)), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        _ = (
            await fixture.Scenarios.RunDefaultAsync(
                editor.Draft(new(fixture.HostId)),
                trigger.Id,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
        (
            await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationDispatchStatus.InvalidFlow);
        fixture.Chat.Calls.ShouldBe(0);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (
                await db.AutomationFlowNodes.SingleAsync(node => node.Id == send.Id.Value)
            ).InputBindingsJson.ShouldBe(unsupportedBindings);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        }
        var history = new AutomationEditorHistory();
        history.StartLoaded(editor);
        editor
            .Nodes.Single(node => node.Id == send.Id)
            .SetBindingMode(new("message"), AutomationInputBindingMode.Fixed);
        history.Record(editor).ShouldBeTrue();
        _ = (
            await fixture.Flows.ValidateDraftAsync(
                editor.Draft(new(fixture.HostId)),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Valid>();
        var undone = history.Undo(editor).ShouldNotBeNull();
        undone
            .Nodes.Single(node => node.Id == send.Id)
            .Binding(new("message"))
            .Expression!.Source.ShouldBe("actor.display_name");
        _ = (
            await fixture.Flows.ValidateDraftAsync(
                undone.Draft(new(fixture.HostId)),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Invalid>();
        editor = history.Redo(undone).ShouldNotBeNull();
        _ = (
            await fixture.Flows.SaveAsync(editor.Draft(new(fixture.HostId)), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        _ = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        fixture.Chat.Messages.ShouldBe(["Retained fixed\nmessage"]);
    }

    [Test]
    public async Task SendChat_UnsupportedFrozenExpressionFailsBeforePublicEffect()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not send"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.SingleAsync(row => row.Id == runId.Value);
            var frozen = AutomationRuntimeSerialization
                .RestoreDefinition(run.DefinitionJson)
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>()
                .Flow;
            run.DefinitionJson = AutomationRuntimeSerialization.SerializeDefinition(
                frozen with
                {
                    Nodes =
                    [
                        .. frozen.Nodes.Select(node =>
                            node.Id == action.Id.Value
                                ? node with
                                {
                                    InputBindingsJson =
                                        AutomationRuntimeSerialization.SerializeInputBindings(
                                            Bindings(
                                                "message",
                                                AutomationInputBindingMode.Expression,
                                                new(new(1), "'must not execute'")
                                            )
                                        ),
                                }
                                : node
                        ),
                    ],
                }
            );
            _ = await db.SaveChangesAsync();
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Failed
        );
        fixture.Chat.Calls.ShouldBe(0);
        await using var verify = await fixture.Database.CreateDbContextAsync();
        var failed = await verify
            .AutomationFlowRuns.Include(row => row.NodeRuns)
            .SingleAsync(row => row.Id == runId.Value);
        failed.Status.ShouldBe(AutomationFlowRunStatus.Failed);
        failed.NodeRuns.ShouldContain(node => node.OutcomeCode == "definition-invalid");
    }

    [Test]
    public async Task BindingRepair_RequiredConnectedNumberHasNoFabricatedZeroAndExecutesFrozenConsumer()
    {
        await using var fixture = await RuntimeFixture.CreateAsync(
            integerEntropy: new CountingIntegerEntropy(73)
        );
        var graph = BindingRepairGraph(fixture.HostId);
        _ = (
            await fixture.Flows.ValidateDraftAsync(graph, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Valid>();
        var id = (await fixture.Flows.SaveAsync(graph, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var read = (
            await fixture.Flows.ReadForAuthoringAsync(
                new(fixture.HostId),
                id,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Editor>();
        var transform = read.Snapshot.Draft.Nodes.Single(node =>
            node.Definition.TypeId == "cel-transform"
        );
        transform
            .Definition.Configuration.GetProperty("inputs")[1]
            .GetProperty("fixed")
            .ValueKind.ShouldBe(JsonValueKind.Null);
        var scenario = await fixture.Scenarios.RunDefaultAsync(
            read.Snapshot.Draft,
            graph.Nodes[0].Id,
            CancellationToken.None
        );
        _ = scenario.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        fixture.Chat.Messages.ShouldBeEmpty();
        var dispatch = await fixture.Runtime.DispatchAsync(
            new(
                Context(fixture.HostId, sourceDefinitionId: AutomationDefinitionIds.FollowSource),
                new FollowSourceConfiguration()
            ),
            CancellationToken.None
        );
        dispatch.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        fixture.Chat.Messages.ShouldHaveSingleItem().ShouldContain("73");
        await using var db = await fixture.Database.CreateDbContextAsync();
        var run = await db.AutomationFlowRuns.SingleAsync();
        run.Status.ShouldBe(BlokeBot.Persistence.Models.AutomationFlowRunStatus.Completed);
        run.DefinitionJson.ShouldContain("fixed");
        (
            await db.AutomationNodeRuns.CountAsync(node =>
                node.OutcomeCode == "output-checkpointed"
            )
        ).ShouldBe(2);
    }

    [Test]
    public async Task BindingRepair_RawMalformedSourceRepairPreservesOriginalOnFailureAndExactUntouchedStringsOnSave()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var trigger = Node("custom-command", """{"custom-command-id":7}""");
        var send = Node("send-chat", """{"message":"tea"}""");
        var id = await fixture.SaveAsync([trigger, send], [Edge(trigger, "flow", send)]);
        var peer = await fixture.SaveAsync(
            [Node("custom-command", """{"custom-command-id":7}""")],
            []
        );
        const string Bad = "{ this nested configuration is not JSON\n";
        const string RetainedBindings =
            " { \"message\" : { \"mode\" : \"Fixed\", \"expression\" : null } } ";
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var row = await db.AutomationFlowNodes.SingleAsync(node => node.Id == send.Id.Value);
            row.ConfigurationJson = Bad;
            row.InputBindingsJson = RetainedBindings;
            _ = await db.SaveChangesAsync();
        }
        var list = (
            await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowQueryOutcome.Available>();
        list.Flows.ShouldHaveSingleItem().Draft.Id.ShouldBe(peer);
        list.AuthoringEntries.ShouldHaveSingleItem().Id.ShouldBe(id);
        var read = (
            await fixture.Flows.ReadForAuthoringAsync(
                new(fixture.HostId),
                id,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Json>();
        read.Original.Nodes.Single(node => node.Id == send.Id.Value)
            .ConfigurationJson.ShouldBe(Bad);
        _ = (
            await fixture.Flows.SaveSourceAsync(
                new(fixture.HostId),
                id,
                AutomationAuthoredFlowCodec.Write(read.Original),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (
                await db.AutomationFlowNodes.SingleAsync(node => node.Id == send.Id.Value)
            ).ConfigurationJson.ShouldBe(Bad);
        }

        var repaired = read.Original with
        {
            Nodes =
            [
                .. read.Original.Nodes.Select(node =>
                    node.Id == send.Id.Value
                        ? node with
                        {
                            ConfigurationJson = " { \"message\" : \"repaired tea\" } ",
                        }
                        : node
                ),
            ],
        };
        var text = AutomationAuthoredFlowCodec.Write(repaired);
        _ = (
            await fixture.Flows.ValidateSourceAsync(
                new(fixture.HostId),
                id,
                text,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSourceValidationOutcome.Valid>();
        (await fixture.Flows.SaveSourceAsync(new(fixture.HostId), id, text, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId.ShouldBe(id);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var row = await db.AutomationFlowNodes.SingleAsync(node => node.Id == send.Id.Value);
            row.ConfigurationJson.ShouldBe(" { \"message\" : \"repaired tea\" } ");
            row.InputBindingsJson.ShouldBe(RetainedBindings);
            (
                await db.AutomationFlows.SingleAsync(flow => flow.Id == id.Value)
            ).IsEnabled.ShouldBeTrue();
        }
        _ = (
            await fixture.Flows.ReadForAuthoringAsync(
                new(fixture.HostId + 1),
                id,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Unavailable>();
        (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Length.ShouldBe(2);
    }

    [Test]
    public async Task BindingRepair_InvalidOutputRemainsEditableAndCannotRebindSurvivingInputIdentity()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var graph = BindingRepairGraph(fixture.HostId);
        var id = (await fixture.Flows.SaveAsync(graph, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var transform = graph.Nodes.Single(node => node.Definition.TypeId == "cel-transform");
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var row = await db.AutomationFlowNodes.SingleAsync(node =>
                node.Id == transform.Id.Value
            );
            row.ConfigurationJson = row.ConfigurationJson.Replace(
                "${actor.display_name} tea ${format_number(tea_index)}",
                "(",
                StringComparison.Ordinal
            );
            _ = await db.SaveChangesAsync();
        }
        var read = (
            await fixture.Flows.ReadForAuthoringAsync(
                new(fixture.HostId),
                id,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Editor>();
        read.Errors.ShouldContain(error =>
            error.NodeId == transform.Id && error.PortId == new AutomationPortId("message")
        );
        var descriptor = fixture
            .Catalog.DescribeForAuthoring(
                new(fixture.HostId),
                read.Snapshot.Draft.Nodes.Single(node => node.Id == transform.Id)
            )
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>()
            .Definition;
        descriptor.Inputs.ShouldContain(port => port.Id == new AutomationPortId("tea-index"));
        var candidate = graph with
        {
            Id = id,
            Nodes =
            [
                .. graph.Nodes.Select(node =>
                    node.Id == transform.Id
                        ? node with
                        {
                            Definition = node.Definition with
                            {
                                Configuration = Persisted(
                                    "cel-transform",
                                    node.Definition.Configuration.GetRawText()
                                        .Replace(
                                            "tea-index-binding",
                                            "replacement-binding",
                                            StringComparison.Ordinal
                                        )
                                ).Configuration,
                            },
                            InputBindings = node
                                .InputBindings.Remove(new("tea-index-binding"))
                                .Add(
                                    new("replacement-binding"),
                                    new(AutomationInputBindingMode.Connected, null)
                                ),
                        }
                        : node
                ),
            ],
        };
        var rejected = (
            await fixture.Flows.SaveAsync(candidate, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        rejected.Errors.ShouldContain(error =>
            error.Code == "transform-input-binding-field-changed"
        );
        await using var unchanged = await fixture.Database.CreateDbContextAsync();
        (
            await unchanged.AutomationFlowNodes.SingleAsync(node => node.Id == transform.Id.Value)
        ).ConfigurationJson.ShouldContain("tea-index-binding");
    }

    [Test]
    public async Task BindingRepair_BlankActiveAndWrongKindCandidatesReturnTypedErrorsWithoutLosingDraft()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var graph = BindingRepairGraph(fixture.HostId);
        var send = graph.Nodes.Single(node => node.Definition.TypeId == "send-chat");
        var blank = graph with
        {
            Edges =
            [
                .. graph.Edges.Where(edge =>
                    edge.TargetNodeId != send.Id || edge.Kind != AutomationEdgeKind.Data
                ),
            ],
            Nodes =
            [
                .. graph.Nodes.Select(node =>
                    node.Id == send.Id
                        ? node with
                        {
                            InputBindings = Bindings(
                                "message",
                                AutomationInputBindingMode.Expression,
                                new(new(1), " ")
                            ),
                        }
                        : node
                ),
            ],
        };
        _ = (
            await fixture.Flows.SaveAsync(blank, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        var transform = graph.Nodes.Single(node => node.Definition.TypeId == "cel-transform");
        var wrong = BindingRepairGraph(fixture.HostId, "not a number");
        _ = (
            await fixture.Flows.SaveAsync(wrong, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlows.CountAsync()).ShouldBe(0);
        graph
            .Nodes.Single(node => node.Id == transform.Id)
            .Definition.Configuration.GetProperty("inputs")[1]
            .GetProperty("fixed")
            .ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task BindingRepair_NonTargetConditionRejectsClearedInactiveExpressionWithoutMutatingExistingRecord()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var trigger = Node("follow", "{}");
        var condition = Node(
            "condition",
            """{"predicate":true}""",
            bindings: Bindings("predicate", AutomationInputBindingMode.Fixed)
        );
        var draft = Draft(fixture.HostId, [trigger, condition], [Edge(trigger, "flow", condition)]);
        var id = (await fixture.Flows.SaveAsync(draft, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        string original;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            original = (
                await db.AutomationFlowNodes.SingleAsync(node => node.Id == condition.Id.Value)
            ).InputBindingsJson;
        }
        var invalid = draft with
        {
            Id = id,
            Nodes =
            [
                trigger,
                condition with
                {
                    InputBindings = Bindings(
                        "predicate",
                        AutomationInputBindingMode.Fixed,
                        new(new(1), "")
                    ),
                },
            ],
        };
        _ = (
            await fixture.Flows.SaveAsync(invalid, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        _ = (
            await fixture.Flows.SaveAsync(invalid with { Id = null }, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        await using var unchanged = await fixture.Database.CreateDbContextAsync();
        (await unchanged.AutomationFlows.CountAsync()).ShouldBe(1);
        (
            await unchanged.AutomationFlowNodes.SingleAsync(node => node.Id == condition.Id.Value)
        ).InputBindingsJson.ShouldBe(original);
    }

    private static AutomationFlowDraft BindingRepairGraph(int hostId, object? numberFixed = null)
    {
        var trigger = Node("follow", "{}");
        var random = Node("random-number", """{"minimum":0,"maximum":100}""");
        var transform = Node(
            "cel-transform",
            TransformJson(
                [
                    new(
                        "actor",
                        "actor",
                        "Actor",
                        "actor-binding",
                        AutomationPortValueType.Actor,
                        AutomationPortNullability.NonNullable,
                        null
                    ),
                    new(
                        "tea-index",
                        "tea_index",
                        "Tea index",
                        "tea-index-binding",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        numberFixed
                    ),
                ],
                [
                    new(
                        "message",
                        "Message",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "${actor.display_name} tea ${format_number(tea_index)}"
                    ),
                ]
            ),
            bindings: Bindings("actor-binding", AutomationInputBindingMode.Connected)
                .Add(new("tea-index-binding"), new(AutomationInputBindingMode.Connected, null))
        );
        var send = Node(
            "send-chat",
            """{"message":""}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected, new(new(1), "("))
        );
        return Draft(
            hostId,
            [trigger, random, transform, send],
            [
                Edge(trigger, "flow", send),
                Edge(trigger, "actor", transform, "actor", AutomationEdgeKind.Data),
                Edge(random, "number", transform, "tea-index", AutomationEdgeKind.Data),
                Edge(transform, "message", send, "message", AutomationEdgeKind.Data),
            ]
        );
    }
}
