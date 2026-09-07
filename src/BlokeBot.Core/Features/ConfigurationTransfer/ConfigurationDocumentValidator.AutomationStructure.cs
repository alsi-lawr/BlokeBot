using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationDocumentValidator
{
    private static ConfigurationValidationIssue? ValidateAutomationStructure(
        AutomationsSectionV2 section
    )
    {
        const string Path = "sections.automations";
        var issue =
            RequiredAutomationElements(section.Flows, $"{Path}.flows")
            ?? RequiredAutomationElements(section.Subflows, $"{Path}.subflows")
            ?? RequiredAutomationElements(section.HostReferences, $"{Path}.hostReferences")
            ?? RequiredAutomationElements(section.Scenarios, $"{Path}.scenarios");
        if (issue is not null)
        {
            return issue;
        }
        for (var index = 0; index < section.Flows.Count; index++)
        {
            if (
                ValidateAutomationGraphStructure(section.Flows[index], $"{Path}.flows[{index}]") is
                { } graphIssue
            )
            {
                return graphIssue;
            }
        }
        for (var index = 0; index < section.Subflows.Count; index++)
        {
            var revision = section.Subflows[index];
            var path = $"{Path}.subflows[{index}]";
            issue =
                ValidateAutomationInterfaceStructure(revision.Interface, $"{path}.interface")
                ?? ValidateAutomationGraphStructure(revision.Graph, $"{path}.graph");
            if (issue is not null)
            {
                return issue;
            }
        }
        for (var index = 0; index < section.Scenarios.Count; index++)
        {
            var scenario = section.Scenarios[index];
            var path = $"{Path}.scenarios[{index}]";
            issue =
                RequiredAutomationElements(scenario.Inputs, $"{path}.inputs")
                ?? RequiredAutomationElements(scenario.Effects, $"{path}.effects");
            if (issue is not null)
            {
                return issue;
            }
        }
        return null;
    }

    private static ConfigurationValidationIssue? ValidateAutomationGraphStructure(
        AutomationFlowV2? graph,
        string path
    )
    {
        if (graph is null)
        {
            return new(path, "An automation graph is required.");
        }
        var issue =
            RequiredAutomationElements(graph.Nodes, $"{path}.nodes")
            ?? RequiredAutomationElements(graph.Edges, $"{path}.edges");
        if (issue is not null)
        {
            return issue;
        }
        for (var index = 0; index < graph.Nodes.Count; index++)
        {
            var node = graph.Nodes[index];
            var nodePath = $"{path}.nodes[{index}]";
            issue = RequiredAutomationElements(node.InputBindings, $"{nodePath}.inputBindings");
            if (issue is not null)
            {
                return issue;
            }
            if (
                node.Subflow is { } binding
                && ValidateAutomationInterfaceStructure(
                    binding.Interface,
                    $"{nodePath}.subflow.interface"
                )
                    is { } interfaceIssue
            )
            {
                return interfaceIssue;
            }
        }
        return null;
    }

    private static ConfigurationValidationIssue? ValidateAutomationInterfaceStructure(
        AutomationSubflowInterface? contract,
        string path
    ) =>
        contract is null
            ? new(path, "A subflow interface is required.")
            : ValidateAutomationPortStructure(contract.Inputs, $"{path}.inputs")
                ?? ValidateAutomationPortStructure(contract.Outputs, $"{path}.outputs");

    private static ConfigurationValidationIssue? ValidateAutomationPortStructure(
        ImmutableArray<AutomationPortMetadata> ports,
        string path
    )
    {
        if (ports.IsDefault)
        {
            return new(path, "A subflow port collection is required.");
        }
        if (RequiredAutomationElements(ports, path) is { } issue)
        {
            return issue;
        }
        for (var index = 0; index < ports.Length; index++)
        {
            var port = ports[index];
            if (string.IsNullOrWhiteSpace(port.Id.Value))
            {
                return new($"{path}[{index}].id", "A subflow port ID is required.");
            }
            if (string.IsNullOrWhiteSpace(port.Name))
            {
                return new($"{path}[{index}].name", "A subflow port name is required.");
            }
            if (string.IsNullOrWhiteSpace(port.Description))
            {
                return new(
                    $"{path}[{index}].description",
                    "A subflow port description is required."
                );
            }
        }
        return null;
    }

    private static ConfigurationValidationIssue? RequiredAutomationElements<T>(
        IReadOnlyList<T>? values,
        string path
    )
        where T : class
    {
        if (values is null)
        {
            return new(path, "An automation collection is required.");
        }
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is null)
            {
                return new(
                    $"{path}[{index}]",
                    "An automation collection item must be an object, not null."
                );
            }
        }
        return null;
    }
}
