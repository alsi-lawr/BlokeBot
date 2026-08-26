using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class PluginAutomationCatalogRegistry
{
    private static ImmutableDictionary<
        PluginAutomationDefinitionId,
        IPluginAutomationDefinition
    > BuildDefinitions(PluginFeatureDeclaration declaration, PluginFeatureState state)
    {
        var definitions = ImmutableDictionary.CreateBuilder<
            PluginAutomationDefinitionId,
            IPluginAutomationDefinition
        >();
        var registeredIds = new HashSet<AutomationDefinitionId>();
        foreach (
            var definition in declaration.Manifest.AutomationDefinitions.Where(candidate =>
                candidate.FeatureId == state.Key.FeatureId
            )
        )
        {
            ValidateDefinitionShape(definition);
            var descriptor = Descriptor(declaration, state, definition);
            if (!registeredIds.Add(descriptor.Id))
            {
                throw new PluginAutomationPlanException(
                    $"Plugin automation definition '{definition.Id.Value}' collides after namespacing."
                );
            }
            AutomationDefinitionCatalog.Validate(
                descriptor,
                new($"plugin.{declaration.Installation.PluginId.Value}")
            );
            definitions.Add(
                definition.Id,
                new PluginAutomationDefinition(new(declaration, state, definition), descriptor)
            );
        }
        return definitions.ToImmutable();
    }

    private static AutomationDefinitionDescriptor Descriptor(
        PluginFeatureDeclaration declaration,
        PluginFeatureState state,
        PluginAutomationDefinitionDescriptor definition
    )
    {
        var kind = Kind(definition.Kind);
        var inputs = ImmutableArray.CreateBuilder<AutomationPortMetadata>();
        var outputs = ImmutableArray.CreateBuilder<AutomationPortMetadata>();
        if (kind is AutomationNodeKind.Action or AutomationNodeKind.Control)
        {
            inputs.Add(FlowPort("flow", "Flow", "Starts this plugin node."));
            outputs.Add(FlowPort("complete", "Complete", "Continues after this plugin node."));
        }
        else if (kind == AutomationNodeKind.Source)
        {
            outputs.Add(FlowPort("next", "Next", "Continues from this plugin trigger."));
        }

        inputs.AddRange(definition.Inputs.Select(static input => DataPort(input, input: true)));
        outputs.AddRange(
            definition.Outputs.Select(static output => DataPort(output, input: false))
        );
        var provenance = Provenance(declaration, state, definition, null, null);
        return new(
            DefinitionId(declaration.Installation.PluginId, definition.Id),
            kind,
            AutomationDefinitionScope.Host,
            new(new(1), new(1)),
            new(definition.Name, definition.Description, $"Plugin · {declaration.Manifest.Name}"),
            inputs.ToImmutable(),
            outputs.ToImmutable(),
            [
                .. definition.Inputs.Select(
                    static input => new AutomationConfigurationFieldMetadata(
                        new(input.Id.Value),
                        input.Name,
                        "Retains the fixed or connected plugin input value.",
                        new AutomationConfigurationFieldType.Data(
                            PluginAutomationDefinition.ValueType(input.ValueKind)
                        ),
                        input.Required
                    )
                ),
            ],
            kind == AutomationNodeKind.Action
                ? AutomationActionCapabilities.RunsScripts
                : AutomationActionCapabilities.None,
            kind == AutomationNodeKind.Action
                ? AutomationActionRetrySafety.Unsafe
                : AutomationActionRetrySafety.NotApplicable,
            PluginProvenance: provenance
        );
    }

    private static void ValidateDefinitionShape(PluginAutomationDefinitionDescriptor definition)
    {
        var valid = definition.Kind switch
        {
            PluginAutomationDefinitionKind.Source => definition.Inputs.IsEmpty,
            PluginAutomationDefinitionKind.Action => definition.Outputs.IsEmpty,
            PluginAutomationDefinitionKind.Value => definition.Inputs.IsEmpty
                && !definition.Outputs.IsEmpty,
            PluginAutomationDefinitionKind.Control => definition.Outputs.IsEmpty,
            PluginAutomationDefinitionKind.Transform => !definition.Outputs.IsEmpty,
        };
        if (!valid)
        {
            throw new PluginAutomationPlanException(
                $"Plugin automation definition '{definition.Id.Value}' has an invalid shape for {definition.Kind}."
            );
        }
    }

    private static AutomationPortMetadata DataPort(
        PluginAutomationFieldDescriptor field,
        bool input
    ) =>
        new(
            new(field.Id.Value),
            field.Name,
            input ? "Receives typed plugin data." : "Supplies typed plugin data.",
            PluginAutomationDefinition.ValueType(field.ValueKind),
            Nullability: field.Required
                ? AutomationPortNullability.NonNullable
                : AutomationPortNullability.Nullable,
            BindingFieldId: input ? new(field.Id.Value) : null
        );

    private static AutomationPortMetadata FlowPort(string id, string name, string description) =>
        new(new(id), name, description, AutomationPortValueType.Flow);

    private static AutomationPluginProvenance Provenance(
        PluginFeatureDeclaration declaration,
        PluginFeatureState state,
        PluginAutomationDefinitionDescriptor definition,
        string? templateId,
        string? templateHash
    ) =>
        new(
            declaration.Installation.PluginId.Value,
            declaration.Installation.Release.DeclaredVersion.Value,
            declaration.Installation.Release.Tag.Value,
            declaration.Manifest.ManifestVersion,
            state.Key.FeatureId.Value,
            definition.Id.Value,
            Hash(definition),
            state.Fence.OperationId.Value,
            checked((long)state.Fence.Generation.Value),
            checked((long)state.Generation.Value),
            templateId,
            templateHash
        );

    internal static AutomationDefinitionId DefinitionId(
        PluginId pluginId,
        PluginAutomationDefinitionId definitionId
    ) => new($"plugin.{Canonical(pluginId.Value)}.{Canonical(definitionId.Value)}");

    private static string Canonical(string value) => value.Replace('_', '-');

    private static AutomationNodeKind Kind(PluginAutomationDefinitionKind kind) =>
        kind switch
        {
            PluginAutomationDefinitionKind.Source => AutomationNodeKind.Source,
            PluginAutomationDefinitionKind.Action => AutomationNodeKind.Action,
            PluginAutomationDefinitionKind.Value => AutomationNodeKind.Value,
            PluginAutomationDefinitionKind.Control => AutomationNodeKind.Control,
            PluginAutomationDefinitionKind.Transform => AutomationNodeKind.Transform,
        };
}
