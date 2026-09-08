using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task Authoring_TargetChangeRetainsTypedValuesAndBindingsUntilExplicitSameTextRepair()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var text = SubflowPort("message", AutomationPortValueType.Text);
        var nullable = SubflowPort("optional", AutomationPortValueType.Text) with
        {
            Nullability = AutomationPortNullability.Nullable,
        };
        var expression = SubflowPort("calculated", AutomationPortValueType.Text);
        var original = await Publish(
            service,
            Subflow(fixture.HostId, new([text, nullable, expression], []))
        );
        var caller = Caller(
            fixture.HostId,
            original,
            ImmutableDictionary<AutomationPortId, AutomationValue>
                .Empty.Add(text.Id, new AutomationValue.Text("1"))
                .Add(nullable.Id, new AutomationValue.Null(AutomationPortValueType.Text))
        );
        caller = caller with
        {
            Nodes = caller.Nodes.SetItem(
                1,
                caller.Nodes[1] with
                {
                    DisplayAlias = "Keep my label",
                    FailurePolicy = AutomationNodeFailurePolicy.Continue,
                    Position = new(new(312), new(218)),
                    InputBindings = caller
                        .Nodes[1]
                        .InputBindings.SetItem(
                            new("calculated"),
                            new(
                                AutomationInputBindingMode.Expression,
                                new(AutomationExpressionLanguage.CurrentVersion, "'kept'")
                            )
                        ),
                }
            ),
        };
        var editor = AuthoringEditor(caller, fixture.Catalog);
        var history = new AutomationEditorHistory();
        history.StartLoaded(editor);
        var before = editor.Draft(new(fixture.HostId));
        var changed = await Publish(
            service,
            Subflow(
                fixture.HostId,
                new(
                    [
                        text with
                        {
                            ValueType = AutomationPortValueType.Number,
                        },
                        nullable with
                        {
                            Nullability = AutomationPortNullability.NonNullable,
                        },
                        expression with
                        {
                            ValueType = AutomationPortValueType.Number,
                        },
                    ],
                    []
                )
            ) with
            {
                Id = original.SubflowId,
            }
        );
        editor
            .Nodes[1]
            .ReplaceSubflowDefinition(
                AutomationSubflowDefinitions.Invocation(changed),
                fixture.Catalog
            );
        history.Record(editor).ShouldBeTrue();
        history.UndoCount.ShouldBe(1);
        var node = editor.Nodes[1];
        node.Id.ShouldBe(before.Nodes[1].Id);
        node.Position.ShouldBe(before.Nodes[1].Position);
        node.DisplayAlias.ShouldBe(before.Nodes[1].DisplayAlias);
        node.FailurePolicy.ShouldBe(before.Nodes[1].FailurePolicy);
        node.Binding(new("calculated")).ShouldBe(before.Nodes[1].InputBindings[new("calculated")]);
        node.SubflowFixedValue(text.Id).ShouldBe(new AutomationValue.Text("1"));
        node.SubflowFixedValue(nullable.Id)
            .ShouldBe(new AutomationValue.Null(AutomationPortValueType.Text));
        editor.Edges.ShouldBe(before.Edges);
        var invalid = await fixture.Flows.ValidateAsync(
            editor.Draft(new(fixture.HostId)),
            CancellationToken.None
        );
        invalid.Errors.ShouldContain(error =>
            error.Code == "subflow-fixed-type" && error.PortId == text.Id
        );
        invalid.Errors.ShouldContain(error =>
            error.Code == "subflow-fixed-type" && error.PortId == nullable.Id
        );
        editor = history.Undo(editor).ShouldNotBeNull();
        JsonElement
            .DeepEquals(
                editor.Nodes[1].Draft().Definition.Configuration,
                before.Nodes[1].Definition.Configuration
            )
            .ShouldBeTrue();
        editor = history.Redo(editor).ShouldNotBeNull();
        node = editor.Nodes[1];
        using var ui = new BunitContext();
        var input = ui.Render<AutomationSubflowFixedInput>(parameters =>
            parameters
                .Add(value => value.Node, node)
                .Add(value => value.Port, node.Definition.Inputs.Single(port => port.Id == text.Id))
                .Add(value => value.Changed, () => history.Record(editor))
        );
        input.Find("input").GetAttribute("value").ShouldBe("1");
        input.Find("[data-automation-set-fixed-value]").Click();
        node.SubflowFixedValue(text.Id).ShouldBe(new AutomationValue.Number(1));
        history.UndoCount.ShouldBe(2);
        input.Find("input").Input("not a number");
        input.Find("input").Change("not a number");
        node.SubflowFixedValue(text.Id).ShouldBe(new AutomationValue.Number(1));
        _ = input.Find("[role=alert]").ShouldNotBeNull();
        history.UndoCount.ShouldBe(2);
        editor = history.Undo(editor).ShouldNotBeNull();
        editor.Nodes[1].SubflowFixedValue(text.Id).ShouldBe(new AutomationValue.Text("1"));
    }

    [Test]
    public async Task Authoring_RemovedInputsRemainCanonicalDraftFactsThroughHistoryUntilExplicitRemoval()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var value = SubflowPort("message", AutomationPortValueType.Text);
        var calculated = SubflowPort("calculated", AutomationPortValueType.Text);
        var original = await Publish(
            service,
            Subflow(fixture.HostId, new([value, calculated], []))
        );
        var caller = Caller(
            fixture.HostId,
            original,
            ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                value.Id,
                new AutomationValue.Text("Retain me")
            )
        );
        caller = caller with
        {
            Nodes = caller.Nodes.SetItem(
                1,
                caller.Nodes[1] with
                {
                    InputBindings = caller
                        .Nodes[1]
                        .InputBindings.SetItem(
                            new("calculated"),
                            new(
                                AutomationInputBindingMode.Expression,
                                new(AutomationExpressionLanguage.CurrentVersion, "'authored'")
                            )
                        ),
                }
            ),
        };
        var editor = AuthoringEditor(caller, fixture.Catalog);
        var history = new AutomationEditorHistory();
        history.StartLoaded(editor);
        var current = await Publish(
            service,
            Subflow(fixture.HostId) with
            {
                Id = original.SubflowId,
            }
        );
        editor
            .Nodes[1]
            .ReplaceSubflowDefinition(
                AutomationSubflowDefinitions.Invocation(current),
                fixture.Catalog
            );
        history.Record(editor).ShouldBeTrue();
        var validation = await fixture.Flows.ValidateAsync(
            editor.Draft(new(fixture.HostId)),
            CancellationToken.None
        );
        validation.Errors.ShouldContain(error => error.Code == "subflow-fixed-type");
        validation.Errors.ShouldContain(error => error.Code == "binding-field-invalid");
        editor = history.Undo(editor).ShouldNotBeNull();
        editor = history.Redo(editor).ShouldNotBeNull();
        editor.Nodes[1].SubflowFixedValue(value.Id).ShouldBe(new AutomationValue.Text("Retain me"));
        editor
            .Nodes[1]
            .SubflowInputBinding(new("calculated"))!
            .Expression!.Source.ShouldBe("'authored'");
        using var ui = new BunitContext();
        var inspector = ui.Render<AutomationNodeInspector>(parameters =>
            parameters
                .Add(item => item.Node, editor.Nodes[1])
                .Add(item => item.Nodes, editor.Nodes)
                .Add(item => item.Edges, editor.Edges)
                .Add(item => item.Changed, () => history.Record(editor))
        );
        inspector.Find(".automation-unmatched-input button").Click();
        inspector.Find(".automation-unmatched-input button").Click();
        editor.Nodes[1].UnmatchedSubflowInputs.ShouldBeEmpty();
        (
            await fixture.Flows.ValidateAsync(
                editor.Draft(new(fixture.HostId)),
                CancellationToken.None
            )
        ).Errors.ShouldBeEmpty();
        editor = history.Undo(editor).ShouldNotBeNull();
        _ = editor.Nodes[1].UnmatchedSubflowInputs.ShouldHaveSingleItem();
    }

    [Test]
    public async Task Authoring_TargetChangeKeepsSensitiveDataEdgesVisibleForCanonicalRepair()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var input = SubflowPort("time", AutomationPortValueType.Timestamp) with
        {
            Sensitivity = AutomationDataSensitivity.Sensitive,
            Nullability = AutomationPortNullability.Nullable,
        };
        var original = await Publish(service, Subflow(fixture.HostId, new([input], [])));
        var caller = Caller(fixture.HostId, original);
        caller = caller with
        {
            Nodes = caller.Nodes.SetItem(
                1,
                caller.Nodes[1] with
                {
                    InputBindings = caller
                        .Nodes[1]
                        .InputBindings.SetItem(
                            new("time"),
                            new(AutomationInputBindingMode.Connected, null)
                        ),
                }
            ),
            Edges = caller.Edges.Add(
                Edge(
                    caller.Nodes[0],
                    "event-time",
                    caller.Nodes[1],
                    "time",
                    AutomationEdgeKind.Data
                )
            ),
        };
        var editor = AuthoringEditor(caller, fixture.Catalog);
        (
            await fixture.Flows.ValidateAsync(
                editor.Draft(new(fixture.HostId)),
                CancellationToken.None
            )
        ).Errors.ShouldBeEmpty();
        var changed = await Publish(
            service,
            Subflow(
                fixture.HostId,
                new([input with { Sensitivity = AutomationDataSensitivity.Safe }], [])
            ) with
            {
                Id = original.SubflowId,
            }
        );
        editor
            .Nodes[1]
            .ReplaceSubflowDefinition(
                AutomationSubflowDefinitions.Invocation(changed),
                fixture.Catalog
            );
        editor.Edges.ShouldBe(caller.Edges);
        editor.Nodes[1].Binding(new("time")).Mode.ShouldBe(AutomationInputBindingMode.Connected);
        (
            await fixture.Flows.ValidateAsync(
                editor.Draft(new(fixture.HostId)),
                CancellationToken.None
            )
        ).Errors.ShouldNotBeEmpty();
        var compatible = await Publish(
            service,
            Subflow(fixture.HostId, new([input], [])) with
            {
                Id = original.SubflowId,
            }
        );
        editor
            .Nodes[1]
            .ReplaceSubflowDefinition(
                AutomationSubflowDefinitions.Invocation(compatible),
                fixture.Catalog
            );
        editor.Edges.ShouldBe(caller.Edges);
        (
            await fixture.Flows.ValidateAsync(
                editor.Draft(new(fixture.HostId)),
                CancellationToken.None
            )
        ).Errors.ShouldBeEmpty();
    }

    [Test]
    public async Task Authoring_ComplexInputRepairRejectsInvalidJsonWithoutFallbackAndKeepsSafeRelaxations()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var service = Subflows(fixture);
        var text = SubflowPort("value", AutomationPortValueType.Text);
        var original = await Publish(service, Subflow(fixture.HostId, new([text], [])));
        var node = AuthoringEditor(
            Caller(
                fixture.HostId,
                original,
                ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                    text.Id,
                    new AutomationValue.Text("authored")
                )
            ),
            fixture.Catalog
        ).Nodes[1];
        var relaxed = await Publish(
            service,
            Subflow(
                fixture.HostId,
                new(
                    [
                        text with
                        {
                            Nullability = AutomationPortNullability.Nullable,
                            Sensitivity = AutomationDataSensitivity.Sensitive,
                        },
                    ],
                    []
                )
            ) with
            {
                Id = original.SubflowId,
            }
        );
        node.ReplaceSubflowDefinition(
            AutomationSubflowDefinitions.Invocation(relaxed),
            fixture.Catalog
        );
        node.SubflowFixedValue(text.Id).ShouldBe(new AutomationValue.Text("authored"));
        var changed = await Publish(
            service,
            Subflow(
                fixture.HostId,
                new([text with { ValueType = AutomationPortValueType.Actor }], [])
            ) with
            {
                Id = original.SubflowId,
            }
        );
        node.ReplaceSubflowDefinition(
            AutomationSubflowDefinitions.Invocation(changed),
            fixture.Catalog
        );
        node.TrySetSubflowFixedValue(text.Id, "{}").ShouldBeFalse();
        node.SubflowFixedValue(text.Id).ShouldBe(new AutomationValue.Text("authored"));
        var actor = AutomationEditorNode.DefaultFixtureValue(AutomationPortValueType.Actor);
        node.TrySetSubflowFixedValue(text.Id, AutomationEditorNode.DisplayFixedValue(actor))
            .ShouldBeTrue();
        node.SubflowFixedValue(text.Id).ShouldBe(actor);
    }
}
