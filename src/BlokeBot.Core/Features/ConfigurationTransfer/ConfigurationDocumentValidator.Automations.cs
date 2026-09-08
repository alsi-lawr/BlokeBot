using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationDocumentValidator
{
    internal static ConfigurationValidationIssue? ValidateAutomations(AutomationsSectionV2? section)
    {
        if (section is null)
        {
            return null;
        }
        var issue =
            ValidateAutomationStructure(section)
            ?? Limit("sections.automations.flows", section.Flows.Count)
            ?? Limit("sections.automations.scenarios", section.Scenarios.Count)
            ?? Limit("sections.automations.hostReferences", section.HostReferences.Count)
            ?? DuplicateIds("sections.automations.flows", section.Flows.Select(value => value.Id))
            ?? DuplicateIds(
                "sections.automations.hostReferences",
                section.HostReferences.Select(value => value.Id)
            );
        if (issue is not null)
        {
            return issue;
        }
        try
        {
            _ = AutomationPortableDependencies.Order(section.Subflows, section.Flows);
        }
        catch (AutomationConfigurationExportException exception)
        {
            return new("sections.automations.subflows", exception.Reason);
        }
        if (
            section.Flows.Select(flow => flow.Name.Trim().ToLowerInvariant()).Distinct().Count()
            != section.Flows.Count
        )
        {
            return new("sections.automations.flows", "Use distinct flow names.");
        }
        if (
            section.Subflows.Any(revision =>
                string.IsNullOrWhiteSpace(revision.Id)
                || string.IsNullOrWhiteSpace(revision.SubflowId)
                || revision.Revision < 1
                || revision.Description.Length > 2000
                || revision.Graph.Enabled
                || !AutomationSubflowDefinitions.ValidInterface(revision.Interface)
            )
            || section
                .Subflows.Select(revision => (revision.SubflowId, revision.Revision))
                .Distinct()
                .Count() != section.Subflows.Count
        )
        {
            return new(
                "sections.automations.subflows",
                "Use unique valid immutable subflow revisions and interfaces."
            );
        }
        if (
            section
                .Scenarios.GroupBy(scenario => scenario.FlowId)
                .Any(group =>
                    group.Count() > AutomationScenarioService.MaximumScenariosPerFlow
                    || group.Select(scenario => scenario.Name.Trim()).Distinct().Count()
                        != group.Count()
                )
            || section.Scenarios.Select(scenario => scenario.Id).Distinct().Count()
                != section.Scenarios.Count
            || section.Scenarios.Any(scenario =>
                string.IsNullOrWhiteSpace(scenario.Id)
                || string.IsNullOrWhiteSpace(scenario.Name)
                || scenario.Name.Length > 200
                || !section.Flows.Any(flow => flow.Id == scenario.FlowId)
                || scenario.Inputs.Count > 1024
                || scenario.Effects.Count > 256
            )
        )
        {
            return new(
                "sections.automations.scenarios",
                "Use distinct named scenarios belonging to included flows, within the scenario limits."
            );
        }
        foreach (
            var flow in section.Flows.Concat(section.Subflows.Select(revision => revision.Graph))
        )
        {
            var path = $"sections.automations.flows[{flow.Id}]";
            if (flow.Nodes.Count > 256 || flow.Edges.Count > 1024)
            {
                return new(path, "Reduce this graph to the supported node and edge limits.");
            }
            issue =
                Limit($"{path}.nodes", flow.Nodes.Count)
                ?? Limit($"{path}.edges", flow.Edges.Count)
                ?? DuplicateIds($"{path}.nodes", flow.Nodes.Select(value => value.Id))
                ?? DuplicateIds($"{path}.edges", flow.Edges.Select(value => value.Id));
            if (issue is not null)
            {
                return issue;
            }
            if (string.IsNullOrWhiteSpace(flow.Id) || string.IsNullOrWhiteSpace(flow.Name))
            {
                return new(path, "An automation flow requires an export-local ID and name.");
            }
            var nodeIds = flow.Nodes.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
            if (
                flow.Edges.Any(edge =>
                    !nodeIds.Contains(edge.SourceNodeId) || !nodeIds.Contains(edge.TargetNodeId)
                )
            )
            {
                return new(
                    $"{path}.edges",
                    "Every edge must reference exported nodes in its flow."
                );
            }
            foreach (var node in flow.Nodes)
            {
                var nodePath = $"{path}.nodes[{node.Id}]";
                var isSubflow =
                    node.DefinitionId
                    is AutomationSubflowDefinitions.Entry
                        or AutomationSubflowDefinitions.Exit
                        or AutomationSubflowDefinitions.Invoke;
                if (
                    isSubflow != (node.Subflow is not null)
                    || (
                        node.Subflow is { } binding
                        && (
                            !AutomationSubflowDefinitions.ValidInterface(binding.Interface)
                            || !AutomationPortableConfiguration.ValidFixedInputs(
                                binding.FixedInputsJson
                            )
                            || node.DefinitionId == AutomationSubflowDefinitions.Invoke
                                != (binding.RevisionId is not null)
                            || node.Configuration.ValueKind != JsonValueKind.Object
                            || node.Configuration.EnumerateObject().Any()
                        )
                    )
                )
                {
                    return new(
                        nodePath,
                        "Use the typed subflow binding with an included immutable revision."
                    );
                }
                if (
                    string.IsNullOrWhiteSpace(node.Id)
                    || string.IsNullOrWhiteSpace(node.DefinitionId)
                    || node.DefinitionSchemaVersion <= 0
                    || node.ExpressionLanguageVersion <= 0
                    || node.InputBindings.Any(binding => string.IsNullOrWhiteSpace(binding.FieldId))
                )
                {
                    return new(nodePath, "The automation node contract is invalid.");
                }
                issue =
                    Limit($"{nodePath}.inputBindings", node.InputBindings.Count)
                    ?? DuplicateIds(
                        $"{nodePath}.inputBindings",
                        node.InputBindings.Select(value => value.FieldId)
                    )
                    ?? ValidateBindingShape(nodePath, node.InputBindings)
                    ?? ValidateConfigurationObject(nodePath, node.Configuration);
                if (issue is not null)
                {
                    return issue;
                }
            }
        }
        return null;
    }

    private static ConfigurationValidationIssue? ValidateConfigurationObject(
        string nodePath,
        JsonElement configuration
    ) =>
        configuration.ValueKind == JsonValueKind.Object
            ? null
            : new($"{nodePath}.configuration", "Automation configuration must be a JSON object.");

    private static ConfigurationValidationIssue? ValidateBindingShape(
        string nodePath,
        IEnumerable<AutomationInputBindingV2> bindings
    )
    {
        foreach (var binding in bindings)
        {
            var path = $"{nodePath}.inputBindings[{binding.FieldId}]";
            if (!Enum.IsDefined(binding.Mode))
            {
                return new(path, "Choose Fixed, Connected, or Expression for this input.");
            }
            if (
                binding.Mode == AutomationInputBindingMode.Expression
                    ? binding.ExpressionLanguageVersion is null or <= 0
                        || string.IsNullOrWhiteSpace(binding.Expression)
                    : binding.ExpressionLanguageVersion is not null
                        || binding.Expression is not null
            )
            {
                return new(
                    path,
                    "Expression bindings require one expression and language version; other bindings must omit both."
                );
            }
        }
        return null;
    }
}
