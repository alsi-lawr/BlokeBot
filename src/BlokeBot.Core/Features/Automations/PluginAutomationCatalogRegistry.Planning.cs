using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class PluginAutomationCatalogRegistry
{
    public PluginAutomationPlanOutcome Prepare(
        PluginFeatureDeclaration declaration,
        PluginFeatureDescriptor feature,
        PluginFeatureState state,
        Guid operationId
    )
    {
        if (operationId == Guid.Empty || state.Key.PluginId != declaration.Installation.PluginId)
        {
            return Rejected("Plugin automation enable identity is invalid.");
        }

        try
        {
            var definitions = BuildDefinitions(declaration, state);
            foreach (var definition in definitions.Values)
            {
                if (
                    Current.Definitions.TryGetValue(definition.Descriptor.Id, out var active)
                    && active.Endpoint.Declaration.Installation.PluginId
                        != declaration.Installation.PluginId
                )
                {
                    return Rejected(
                        $"Plugin automation definition '{definition.Descriptor.Id.Value}' collides with an active plugin definition."
                    );
                }
            }
            var templates = ImmutableArray.CreateBuilder<PluginAutomationTemplateStorePlan>();
            foreach (var templateId in feature.AutomationTemplates)
            {
                var template = declaration.Manifest.AutomationTemplates.Single(candidate =>
                    candidate.Id == templateId && candidate.FeatureId == feature.Id
                );
                templates.Add(PrepareTemplate(declaration, state, template, definitions));
            }
            return
                templates
                    .Select(static template => template.Name)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != templates.Count
                ? Rejected(
                    $"Feature '{feature.Id.Value}' declares duplicate automation flow names."
                )
                : new PluginAutomationPlanOutcome.Prepared(
                    new(
                        operationId,
                        declaration.Installation.PluginId.Value,
                        declaration.Installation.Release.DeclaredVersion.Value,
                        declaration.Installation.Release.Tag.Value,
                        declaration.Manifest.ManifestVersion,
                        state.Key.FeatureId.Value,
                        templates.ToImmutable()
                    )
                );
        }
        catch (PluginAutomationPlanException exception)
        {
            return Rejected(exception.Message);
        }
        catch (AutomationCatalogRegistrationException exception)
        {
            return Rejected(exception.Message);
        }
    }

    private static PluginAutomationTemplateStorePlan PrepareTemplate(
        PluginFeatureDeclaration declaration,
        PluginFeatureState state,
        PluginAutomationTemplateDescriptor template,
        IReadOnlyDictionary<PluginAutomationDefinitionId, IPluginAutomationDefinition> definitions
    )
    {
        ValidateTemplateStructure(template, definitions);
        var templateHash = Hash(
            string.Join(
                "\n",
                new[] { Hash(template) }.Concat(
                    template
                        .Nodes.Select(node =>
                            definitions[node.DefinitionId].Descriptor.PluginProvenance!
                        )
                        .Select(static provenance => provenance.DefinitionHash)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                )
            )
        );
        var nodes = ImmutableArray.CreateBuilder<PluginAutomationStoreNode>();
        var nodeIds = template.Nodes.ToDictionary(
            static node => node.Id,
            static _ => Guid.NewGuid()
        );
        foreach (var (node, index) in template.Nodes.Select((node, index) => (node, index)))
        {
            if (!definitions.TryGetValue(node.DefinitionId, out var definition))
            {
                throw new PluginAutomationPlanException(
                    $"Template '{template.Id.Value}' references a definition outside feature '{state.Key.FeatureId.Value}'."
                );
            }

            var configuration = ConfigurationJson(node.Configuration);
            using var document = JsonDocument.Parse(configuration);
            var parsed = definition.Parse(document.RootElement);
            if (
                parsed is not AutomationConfigurationParseResult.Parsed accepted
                || !definition.Validate(accepted.Configuration).IsValid
            )
            {
                throw new PluginAutomationPlanException(
                    $"Template '{template.Id.Value}' has invalid node configuration."
                );
            }

            var incoming = template.Edges.Where(edge => edge.ToNode == node.Id).ToArray();
            if (
                definition.Descriptor.Inputs.Any(input =>
                    input.ValueType != AutomationPortValueType.Flow
                    && input.BindingFieldId is { } fieldId
                    && incoming.All(edge => edge.ToInput.Value != input.Id.Value)
                    && definition
                        .Descriptor.Configuration.Single(field => field.Id == fieldId)
                        .Required
                    && accepted.Configuration is PluginAutomationConfiguration configuration
                    && !configuration.Values.ContainsKey(fieldId)
                )
            )
            {
                throw new PluginAutomationPlanException(
                    $"Template '{template.Id.Value}' has a required input without a fixed value or Data connection."
                );
            }

            var provenance = definition.Descriptor.PluginProvenance! with
            {
                TemplateId = template.Id.Value,
                TemplateHash = templateHash,
            };
            var connected = template
                .Edges.Where(edge => edge.ToNode == node.Id)
                .Select(static edge => new AutomationConfigurationFieldId(edge.ToInput.Value))
                .ToHashSet();
            var bindings = definition.Descriptor.Configuration.ToImmutableDictionary(
                static field => field.Id,
                field => new AutomationInputBinding(
                    connected.Contains(field.Id)
                        ? AutomationInputBindingMode.Connected
                        : AutomationInputBindingMode.Fixed,
                    null
                )
            );
            nodes.Add(
                new(
                    nodeIds[node.Id],
                    definition.Descriptor.Id.Value,
                    definition.Descriptor.Schema.Current.Value,
                    configuration,
                    AutomationRuntimeSerialization.SerializeInputBindings(bindings),
                    SerializeProvenance(provenance),
                    ContinueOnFailure: false,
                    CanvasX: 48 + (index % 3 * 240),
                    CanvasY: 72 + (index / 3 * 168)
                )
            );
        }

        var edges = ImmutableArray.CreateBuilder<PluginAutomationStoreEdge>();
        var executable = template
            .Nodes.Where(node =>
                definitions[node.DefinitionId].Descriptor.Kind
                    is AutomationNodeKind.Source
                        or AutomationNodeKind.Action
                        or AutomationNodeKind.Control
            )
            .ToArray();
        var source = executable.Single(node =>
            definitions[node.DefinitionId].Descriptor.Kind == AutomationNodeKind.Source
        );
        var ordered = new[] { source }.Concat(executable.Where(node => node != source)).ToArray();
        for (var index = 0; index + 1 < ordered.Length; index++)
        {
            edges.Add(
                new(
                    Guid.NewGuid(),
                    PluginAutomationStoreEdgeKind.Flow,
                    nodeIds[ordered[index].Id],
                    index == 0 ? "next" : "complete",
                    nodeIds[ordered[index + 1].Id],
                    "flow"
                )
            );
        }

        foreach (var edge in template.Edges)
        {
            edges.Add(
                new(
                    Guid.NewGuid(),
                    PluginAutomationStoreEdgeKind.Data,
                    nodeIds[edge.FromNode],
                    edge.FromOutput.Value,
                    nodeIds[edge.ToNode],
                    edge.ToInput.Value
                )
            );
        }

        EnsureAcyclic(nodes.Select(static node => node.Id), edges);
        return new(
            template.Name,
            new(
                declaration.Installation.PluginId.Value,
                declaration.Installation.Release.DeclaredVersion.Value,
                declaration.Installation.Release.Tag.Value,
                declaration.Manifest.ManifestVersion,
                state.Key.FeatureId.Value,
                template.Id.Value,
                templateHash
            ),
            nodes.ToImmutable(),
            edges.ToImmutable()
        );
    }

    private static string ConfigurationJson(PluginValue value)
    {
        var map =
            value is PluginValue.Map
            && AutomationStructuredValue.TryConvert(value, out var converted)
            && converted is AutomationValue.Map convertedMap
                ? convertedMap
                : throw new PluginAutomationPlanException(
                    "Plugin automation template configuration must be a bounded JSON map."
                );
        return AutomationStructuredValue.Serialize(map);
    }

    private static PluginAutomationPlanOutcome.Rejected Rejected(string diagnostic) =>
        new(diagnostic);
}
