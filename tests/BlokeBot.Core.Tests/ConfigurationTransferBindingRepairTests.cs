using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    public async Task BindingRepair_PortableConnectedTargetsRetainUnsetFixedAndInactiveExpressionAcrossApplyAndReimport()
    {
        var configuration = JsonSerializer.Deserialize<JsonElement>(
            """
            {"inputs":[{"port-id":"tea-index","cel-identifier":"tea_index","display-name":"Tea index",
            "binding-field-id":"tea-index-binding","type":"Number","nullability":"NonNullable","fixed":null}],
            "outputs":[{"port-id":"message","display-name":"Message","type":"Text","nullability":"NonNullable",
            "cel":"format_number(tea_index)"}]}
            """
        );
        var transform = Node(
            "transform",
            "cel-transform",
            configuration,
            [new("tea-index-binding", AutomationInputBindingMode.Connected, 1, " ")]
        );
        var send = Node(
            "send",
            "send-chat",
            JsonSerializer.Deserialize<JsonElement>("""{"message":""}"""),
            [new("message", AutomationInputBindingMode.Connected, 1, "(")]
        );
        var delay = Node(
            "delay",
            "delay",
            JsonSerializer.Deserialize<JsonElement>("""{"duration-milliseconds":17}""")
        );
        var document = AutomationDocument(
            "Connected tea",
            [
                Node("source", "follow", EmptyObject()),
                Node(
                    "number",
                    "random-number",
                    JsonSerializer.Deserialize<JsonElement>("""{"minimum":23,"maximum":23}""")
                ),
                transform,
                send,
                delay,
            ],
            [
                new("flow", AutomationEdgeKind.Flow, "source", "flow", "delay", "flow"),
                new("then", AutomationEdgeKind.Flow, "delay", "complete", "send", "flow"),
                new(
                    "number-wire",
                    AutomationEdgeKind.Data,
                    "number",
                    "number",
                    "transform",
                    "tea-index"
                ),
                new(
                    "text-wire",
                    AutomationEdgeKind.Data,
                    "transform",
                    "message",
                    "send",
                    "message"
                ),
            ]
        );
        var imported = await DelayTransferCycle(await DelayTransferCycle(document));
        var nodes = imported.Sections.Automations!.Flows.ShouldHaveSingleItem().Nodes;
        var actualTransform = nodes.Single(node => node.DefinitionId == "cel-transform");
        actualTransform.InputBindings.ShouldBe(transform.InputBindings);
        actualTransform
            .Configuration.GetProperty("inputs")[0]
            .GetProperty("fixed")
            .ValueKind.ShouldBe(JsonValueKind.Null);
        var actualSend = nodes.Single(node => node.DefinitionId == "send-chat");
        actualSend.InputBindings.ShouldBe(send.InputBindings);
        actualSend.Configuration.GetProperty("message").GetString().ShouldBeEmpty();
    }
}
