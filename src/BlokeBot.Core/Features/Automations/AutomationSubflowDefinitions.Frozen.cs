using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

public static partial class AutomationSubflowDefinitions
{
    internal static AutomationConfigurationCheck? CheckFrozen(
        PersistedAutomationNodeDefinition node
    )
    {
        if (node.TypeId != Invoke || node.SchemaVersion != 1 || node.PluginProvenance is not null)
        {
            return null;
        }
        try
        {
            var document = node.Configuration.Deserialize<FrozenDocument>(JsonOptions);
            if (
                document is { RevisionId.Value: var id, Interface: not null, FixedInputs: not null }
                && id != Guid.Empty
                && ValidInterface(document.Interface)
                && AutomationDataValueSerialization.RestoreOutputs(document.FixedInputs)
                    is AutomationOutputRestoreOutcome.Available values
            )
            {
                return new AutomationConfigurationCheck.Valid(
                    Descriptor(Invoke, document.Interface),
                    new AutomationFrozenSubflowConfiguration(
                        document.RevisionId!.Value,
                        document.Interface,
                        values.Outputs
                    )
                );
            }
        }
        catch (JsonException) { }
        return new AutomationConfigurationCheck.Invalid(
            ((AutomationConfigurationParseResult.Invalid)Invalid()).Errors
        );
    }

    internal static PersistedAutomationNodeDefinition FreezeInvocation(
        AutomationSubflowInvocationConfiguration configuration,
        AutomationSubflowRevisionId revisionId
    ) =>
        new(
            Invoke,
            1,
            JsonSerializer.SerializeToElement(
                new FrozenDocument(
                    revisionId,
                    configuration.Interface,
                    AutomationDataValueSerialization.SerializeOutputs(configuration.FixedInputs)
                ),
                JsonOptions
            )
        );

    private sealed record FrozenDocument(
        AutomationSubflowRevisionId? RevisionId,
        AutomationSubflowInterface Interface,
        string FixedInputs
    );
}
