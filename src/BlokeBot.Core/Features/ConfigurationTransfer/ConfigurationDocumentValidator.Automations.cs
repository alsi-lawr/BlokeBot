using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationDocumentValidator
{
    private static ConfigurationValidationIssue? ValidateAutomations(AutomationsSectionV1? section)
    {
        if (section is null)
        {
            return null;
        }
        var issue =
            Limit("sections.automations.flows", section.Flows.Count)
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
        foreach (var flow in section.Flows)
        {
            var path = $"sections.automations.flows[{flow.Id}]";
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
        IEnumerable<AutomationInputBindingV1> bindings
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
