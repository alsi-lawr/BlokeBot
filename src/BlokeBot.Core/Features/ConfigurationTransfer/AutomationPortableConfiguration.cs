using System.Text.Json;
using BlokeBot.Core.Features.Automations;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static class AutomationPortableConfiguration
{
    internal static string? Rejection(AutomationConfigurationCheck.Valid validated) =>
        validated.Definition.Configuration.Any(field =>
            field.Sensitivity != AutomationDataSensitivity.Safe
        )
            ? "Sensitive node configuration is not portable."
        : validated.Configuration is AutomationSubflowConfiguration subflow
        && subflow.FixedInputs.Values.Any(value => !PortableValue(value))
            ? "Subflow fixed values contain identifying or derived data."
        : null;

    internal static string? Rejection(string definitionId, JsonElement configuration) =>
        definitionId != AutomationDefinitionIds.CelTransform.Value ? null
        : !AutomationCelTransformDocumentSerializer.TryDeserialize<AutomationCelTransformDocument>(
            configuration,
            out var document
        )
            ? "The CEL configuration schema is invalid."
        : document.Inputs.Count > 1000
        || document.Outputs.Count > 1000
        || document.Inputs.Any(input =>
            input.ValueType == nameof(AutomationPortValueType.Arguments)
            && input.FixedValue.ValueKind == JsonValueKind.Array
            && input.FixedValue.GetArrayLength() > 1000
        )
            ? "A CEL configuration collection exceeds the 1000 record limit."
        : document.Inputs.Any(input =>
            (
                (
                    input.ValueType
                    is nameof(AutomationPortValueType.Actor)
                        or nameof(AutomationPortValueType.Channel)
                        or nameof(AutomationPortValueType.Stream)
                        or nameof(AutomationPortValueType.Arguments)
                )
                && input.FixedValue.ValueKind != JsonValueKind.Null
            ) || HasIdentity(input.FixedValue)
        )
            ? "Identifying or event-derived fixed values are not portable. Remove the fixed value before exporting."
        : null;

    internal static bool ValidFixedInputs(string json)
    {
        if (
            AutomationDataValueSerialization.RestoreOutputs(json)
            is not AutomationOutputRestoreOutcome.Available restored
        )
        {
            return false;
        }
        using var supplied = JsonDocument.Parse(json);
        using var canonical = JsonDocument.Parse(
            AutomationDataValueSerialization.SerializeOutputs(restored.Outputs)
        );
        return JsonElement.DeepEquals(supplied.RootElement, canonical.RootElement)
            && restored.Outputs.Values.All(PortableValue);
    }

    private static bool PortableValue(AutomationResolvedValue value) =>
        value.Provenance.Length == 1
        && value.Provenance[0] == AutomationValueProvenance.Generated
        && !value.ValueFreeDiagnostic
        && value.SafeTriggerFields.IsDefaultOrEmpty
        && SafeValue(value.Value);

    private static bool SafeValue(AutomationValue value) =>
        value switch
        {
            AutomationValue.Actor
            or AutomationValue.Channel
            or AutomationValue.Stream
            or AutomationValue.Arguments => false,
            AutomationValue.Array array => array.Items.All(SafeValue),
            AutomationValue.Map map => map.Properties.All(item =>
                !IdentifyingName(item.Name) && SafeValue(item.Value)
            ),
            _ => true,
        };

    private static bool IdentifyingName(string name) =>
        name.Equals("login", StringComparison.OrdinalIgnoreCase)
        || name.Equals("displayName", StringComparison.OrdinalIgnoreCase)
        || name.Equals("display-name", StringComparison.OrdinalIgnoreCase);

    private static bool HasIdentity(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Object => value
                .EnumerateObject()
                .Any(property => IdentifyingName(property.Name) || HasIdentity(property.Value)),
            JsonValueKind.Array => value.EnumerateArray().Any(HasIdentity),
            _ => false,
        };
}

internal sealed class AutomationConfigurationExportException(
    string definitionId,
    string reason,
    Exception? inner = null
) : Exception(reason, inner)
{
    internal string DefinitionId { get; } = definitionId;
    internal string Reason { get; } = reason;
}
