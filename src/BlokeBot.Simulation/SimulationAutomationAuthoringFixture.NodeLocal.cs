using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;

namespace BlokeBot.Simulation;

internal static partial class SimulationAutomationAuthoringFixture
{
    private static async Task SeedNodeLocalAsync(
        int host,
        AutomationFlowDraftNode source,
        AutomationSubflowService subflows,
        AutomationFlowService flows,
        AutomationCatalogService catalog,
        CancellationToken cancellationToken
    )
    {
        var contract = new AutomationSubflowInterface(
            [new(new("value"), "Value", "Study value", AutomationPortValueType.Text)],
            []
        );
        var id = new AutomationSubflowId(Guid.NewGuid());
        AutomationSubflowDraft Draft(AutomationSubflowInterface current)
        {
            var entry = AutomationEditorNode
                .FromDefinition(
                    AutomationSubflowDefinitions.Boundary(
                        AutomationSubflowDefinitions.Entry,
                        current
                    ),
                    catalog,
                    new(new(60), new(120)),
                    "Entry"
                )
                .Draft();
            var exit = AutomationEditorNode
                .FromDefinition(
                    AutomationSubflowDefinitions.Boundary(
                        AutomationSubflowDefinitions.Exit,
                        current
                    ),
                    catalog,
                    new(new(310), new(120)),
                    "Exit"
                )
                .Draft();
            return new(
                id,
                "Typed input repair fixture",
                current,
                Graph(host, "Typed input study", [entry, exit], [Edge(entry, "complete", exit)])
            );
        }
        var original =
            (
                await subflows.PublishAsync(Draft(contract), cancellationToken)
                as AutomationSubflowPublishOutcome.Published
            )?.Revision
            ?? throw new InvalidOperationException("The typed input fixture is invalid.");
        var caller = AutomationEditorNode
            .FromDefinition(
                AutomationSubflowDefinitions.Invocation(
                    original,
                    ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                        new("value"),
                        new AutomationValue.Text("1")
                    )
                ),
                catalog,
                new(new(310), new(120)),
                "Call subflow"
            )
            .Draft();
        var trigger = source with { Id = new(Guid.NewGuid()) };
        if (
            await flows.SaveAsync(
                Graph(
                    host,
                    "Typed repair study",
                    [trigger, caller],
                    [Edge(trigger, "flow", caller)]
                ),
                cancellationToken
            )
            is not AutomationFlowSaveOutcome.Saved
        )
        {
            throw new InvalidOperationException("The typed input caller is invalid.");
        }
        if (
            await subflows.PublishAsync(
                Draft(
                    contract with
                    {
                        Inputs =
                        [
                            contract.Inputs[0] with
                            {
                                ValueType = AutomationPortValueType.Number,
                            },
                        ],
                    }
                ),
                cancellationToken
            )
            is not AutomationSubflowPublishOutcome.Published
        )
        {
            throw new InvalidOperationException("The changed typed input fixture is invalid.");
        }
    }
}
