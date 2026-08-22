namespace BlokeBot.Plugins.Contracts;

public static partial class PluginManifestValidator
{
    private static void ValidateAutomations(
        PluginManifest manifest,
        List<PluginManifestError> errors
    )
    {
        ValidateCount(manifest.AutomationDefinitions, "$.automationDefinitions", errors);
        ValidateCount(manifest.AutomationTemplates, "$.automationTemplates", errors);
        ValidateDistinct(
            manifest.AutomationDefinitions.Select(definition => definition.Id),
            "$.automationDefinitions",
            errors
        );
        ValidateDistinct(
            manifest.AutomationTemplates.Select(template => template.Id),
            "$.automationTemplates",
            errors
        );

        var featureIds = manifest.Features.Select(feature => feature.Id).ToHashSet();
        var moduleIds = manifest.LuaModules.Select(module => module.Id).ToHashSet();
        foreach (var definition in manifest.AutomationDefinitions)
        {
            ValidateName(definition.Name, "$.automationDefinitions.name", errors);
            ValidateText(
                definition.Description,
                "$.automationDefinitions.description",
                required: true,
                errors
            );
            if (
                !featureIds.Contains(definition.FeatureId)
                || !moduleIds.Contains(definition.Module)
                || !ValidEntryPoint(definition.EntryPoint)
                || HasDuplicateFields(definition.Inputs)
                || HasDuplicateFields(definition.Outputs)
                || definition
                    .Inputs.Select(field => field.Id)
                    .Intersect(definition.Outputs.Select(field => field.Id))
                    .Any()
            )
            {
                errors.Add(
                    new(
                        PluginManifestErrorCode.InvalidAutomationDefinition,
                        "$.automationDefinitions"
                    )
                );
            }
        }

        foreach (var template in manifest.AutomationTemplates)
        {
            ValidateTemplate(template, manifest.AutomationDefinitions, featureIds, errors);
        }
    }

    private static bool HasDuplicateFields(IReadOnlyList<PluginAutomationFieldDescriptor> fields)
    {
        var ids = new HashSet<PluginAutomationFieldId>();
        return fields.Count > PluginContractLimits.MaximumDeclarationsPerSurface
            || fields.Any(field => !ids.Add(field.Id) || string.IsNullOrWhiteSpace(field.Name));
    }

    private static void ValidateTemplate(
        PluginAutomationTemplateDescriptor template,
        IReadOnlyList<PluginAutomationDefinitionDescriptor> definitions,
        IReadOnlySet<PluginFeatureId> featureIds,
        List<PluginManifestError> errors
    )
    {
        ValidateName(template.Name, "$.automationTemplates.name", errors);
        var definitionLookup = definitions
            .GroupBy(definition => definition.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var nodeLookup = new Dictionary<PluginTemplateNodeId, PluginAutomationTemplateNode>();
        var invalid =
            !featureIds.Contains(template.FeatureId)
            || template.Nodes.Length is < 1 or > PluginContractLimits.MaximumDeclarationsPerSurface
            || template.Edges.Length > PluginContractLimits.MaximumDeclarationsPerSurface;

        foreach (var node in template.Nodes)
        {
            invalid |=
                !nodeLookup.TryAdd(node.Id, node)
                || !definitionLookup.ContainsKey(node.DefinitionId)
                || PluginValueValidator.Validate(node.Configuration)
                    is PluginValueValidationOutcome.Invalid;
        }

        foreach (var edge in template.Edges)
        {
            invalid |= !ValidEdge(edge, nodeLookup, definitionLookup);
        }

        var sourceCount = template.Nodes.Count(node =>
            definitionLookup.TryGetValue(node.DefinitionId, out var definition)
            && definition.Kind == PluginAutomationDefinitionKind.Source
        );
        invalid |= sourceCount != 1;

        if (invalid)
        {
            errors.Add(
                new(PluginManifestErrorCode.InvalidAutomationTemplate, "$.automationTemplates")
            );
        }
    }

    private static bool ValidEdge(
        PluginAutomationTemplateEdge edge,
        IReadOnlyDictionary<PluginTemplateNodeId, PluginAutomationTemplateNode> nodes,
        IReadOnlyDictionary<
            PluginAutomationDefinitionId,
            PluginAutomationDefinitionDescriptor
        > definitions
    ) =>
        nodes.TryGetValue(edge.FromNode, out var fromNode)
        && nodes.TryGetValue(edge.ToNode, out var toNode)
        && definitions.TryGetValue(fromNode.DefinitionId, out var fromDefinition)
        && definitions.TryGetValue(toNode.DefinitionId, out var toDefinition)
        && fromDefinition.Outputs.Any(field => field.Id == edge.FromOutput)
        && toDefinition.Inputs.Any(field => field.Id == edge.ToInput);
}
