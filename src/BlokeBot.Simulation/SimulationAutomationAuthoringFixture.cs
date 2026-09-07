using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Simulation;

internal static class SimulationAutomationAuthoringFixture
{
    internal static async Task SeedAsync(
        int host,
        IDbContextFactory<BlokeBotDbContext> database,
        AutomationSubflowService subflows,
        AutomationFlowService flows,
        AutomationScenarioService scenarios,
        AutomationCatalogService catalog,
        CancellationToken cancellationToken
    )
    {
        AutomationFlowDraftNode Node(
            PersistedAutomationNodeDefinition definition,
            string name,
            int x,
            int y
        ) =>
            AutomationEditorNode
                .FromDefinition(definition, catalog, new(new(x), new(y)), name)
                .Draft();
        await using var db = await database.CreateDbContextAsync(cancellationToken);
        if (
            await db.AutomationFlows.AnyAsync(
                flow => flow.HostId == host && flow.Name == "Welcome command",
                cancellationToken
            )
        )
        {
            return;
        }
        var command = await db
            .CustomCommands.Where(value => value.HostId == host)
            .Select(value => value.Id)
            .FirstAsync(cancellationToken);
        var ports = ImmutableArray.Create(
            new AutomationPortMetadata(
                new("message"),
                "Message",
                "Greeting text",
                AutomationPortValueType.Text
            )
        );
        var contract = new AutomationSubflowInterface(ports, ports);
        var entry = Node(
            AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Entry, contract),
            "Entry",
            60,
            100
        );
        var action = Node(
            Definition("send-chat", """{"message":"Welcome!"}"""),
            "Send greeting",
            310,
            100
        ) with
        {
            InputBindings = Connected("message"),
        };
        var exit = Node(
            AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Exit, contract),
            "Exit",
            560,
            100
        ) with
        {
            InputBindings = Connected("message"),
        };
        var leaf = new AutomationSubflowDraft(
            new(Guid.NewGuid()),
            "Send a viewer greeting",
            contract,
            Graph(
                host,
                "Welcome message",
                [entry, action, exit],
                [
                    Edge(entry, "complete", action),
                    Edge(action, "complete", exit),
                    Edge(entry, "message", action, "message", AutomationEdgeKind.Data),
                    Edge(entry, "message", exit, "message", AutomationEdgeKind.Data),
                ]
            )
        );
        AutomationSubflowRevision? revision = null;
        for (var i = 0; i <= AutomationSubflowService.LibraryPageSize; i++)
        {
            revision =
                (
                    await subflows.PublishAsync(leaf, cancellationToken)
                    as AutomationSubflowPublishOutcome.Published
                )?.Revision
                ?? throw new InvalidOperationException("The authoring fixture subflow is invalid.");
        }
        var empty = new AutomationSubflowInterface([], []);
        var outerEntry = Node(
            AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Entry, empty),
            "Entry",
            60,
            100
        );
        var first = Node(
            AutomationSubflowDefinitions.Invocation(
                revision!,
                ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                    new("message"),
                    new AutomationValue.Text("Welcome!")
                )
            ),
            "First greeting",
            280,
            100
        );
        var second = first with
        {
            Id = new(Guid.NewGuid()),
            Position = new(new(500), new(100)),
            DisplayAlias = "Second greeting",
        };
        var outerExit = Node(
            AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Exit, empty),
            "Exit",
            720,
            100
        );
        var outer =
            (
                await subflows.PublishAsync(
                    new(
                        new(Guid.NewGuid()),
                        "Two welcome messages",
                        empty,
                        Graph(
                            host,
                            "Welcome sequence",
                            [outerEntry, first, second, outerExit],
                            [
                                Edge(outerEntry, "complete", first),
                                Edge(first, "complete", second),
                                Edge(second, "complete", outerExit),
                            ]
                        )
                    ),
                    cancellationToken
                ) as AutomationSubflowPublishOutcome.Published
            )?.Revision
            ?? throw new InvalidOperationException("The authoring fixture sequence is invalid.");
        var source = Node(
            Definition(
                "custom-command",
                JsonSerializer.Serialize(
                    new Dictionary<string, int> { ["custom-command-id"] = command }
                )
            ),
            "Chat command",
            60,
            120
        );
        var caller = Node(
            AutomationSubflowDefinitions.Invocation(outer),
            "Welcome sequence",
            310,
            120
        );
        var final = Node(
            Definition("send-chat", """{"message":"Hello, everyone!"}"""),
            "Send announcement",
            560,
            120
        );
        var flow = Graph(
            host,
            "Welcome command",
            [source, caller, final],
            [Edge(source, "flow", caller), Edge(caller, "complete", final)]
        );
        var saved =
            (
                await flows.SaveAsync(flow, cancellationToken) as AutomationFlowSaveOutcome.Saved
            )?.FlowId
            ?? throw new InvalidOperationException("The authoring fixture caller is invalid.");
        var success = scenarios.CreatePortableFixture(flow, source.Id, 42);
        _ = await scenarios.SaveAsync(
            new(host),
            saved,
            null,
            "Welcome succeeds",
            success,
            cancellationToken
        );
        _ = await scenarios.SaveAsync(
            new(host),
            saved,
            null,
            "Announcement fails",
            scenarios.CreatePortableFixture(
                flow,
                source.Id,
                42,
                effects: [new(final.Id, AutomationScenarioEffectResult.Failed)]
            ),
            cancellationToken
        );

        var delay = Node(
            Definition("delay", """{"duration-milliseconds":2000}"""),
            "Wait after greeting",
            560,
            120
        );
        var message = Node(
            Definition(
                "cel-transform",
                """{"inputs":[],"outputs":[{"port-id":"message","display-name":"Message","type":"Text","nullability":"NonNullable","cel":"\"Welcome!\""}]}"""
            ),
            "Compose greeting",
            310,
            320
        );
        var send = final with
        {
            Id = new(Guid.NewGuid()),
            DisplayAlias = "Send greeting",
            Position = new(new(310), new(120)),
            InputBindings = Connected("message"),
        };
        var extractSource = source with { Id = new(Guid.NewGuid()) };
        _ = await flows.SaveAsync(
            Graph(
                host,
                "Extraction study",
                [extractSource, send, delay, message],
                [
                    Edge(extractSource, "flow", send),
                    Edge(send, "complete", delay),
                    Edge(message, "message", send, "message", AutomationEdgeKind.Data),
                ]
            ),
            cancellationToken
        );

        var largeSource = source with { Id = new(Guid.NewGuid()) };
        var actions = Enumerable
            .Range(0, 255)
            .Select(index =>
                final with
                {
                    Id = new(Guid.NewGuid()),
                    DisplayAlias = $"Message {index + 1}",
                    Position = new(new(300 + (index % 8 * 220)), new(index / 8 * 130)),
                }
            )
            .ToArray();
        _ = await flows.SaveAsync(
            Graph(
                host,
                "Large graph study",
                [largeSource, .. actions],
                [.. actions.Select(node => Edge(largeSource, "flow", node))]
            ),
            cancellationToken
        );
    }

    private static AutomationFlowDraft Graph(
        int host,
        string name,
        ImmutableArray<AutomationFlowDraftNode> nodes,
        ImmutableArray<AutomationFlowDraftEdge> edges
    ) => new(null, new(host), name, AutomationFlowSchema.CurrentVersion, false, nodes, edges);

    private static PersistedAutomationNodeDefinition Definition(string id, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, 1, document.RootElement.Clone());
    }

    private static ImmutableDictionary<
        AutomationConfigurationFieldId,
        AutomationInputBinding
    > Connected(string port) =>
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty.Add(
            new(port),
            new(AutomationInputBindingMode.Connected, null)
        );

    private static AutomationFlowDraftEdge Edge(
        AutomationFlowDraftNode source,
        string sourcePort,
        AutomationFlowDraftNode target,
        string targetPort = "flow",
        AutomationEdgeKind kind = AutomationEdgeKind.Flow
    ) => new(Guid.NewGuid(), kind, source.Id, new(sourcePort), target.Id, new(targetPort));
}
