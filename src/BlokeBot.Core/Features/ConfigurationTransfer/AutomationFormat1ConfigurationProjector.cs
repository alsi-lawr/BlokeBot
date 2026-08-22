using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal abstract record AutomationFormat1ConfigurationProjection
{
    private AutomationFormat1ConfigurationProjection() { }

    internal sealed record Projected(
        JsonElement Configuration,
        ImmutableArray<string> RedactionReasons
    ) : AutomationFormat1ConfigurationProjection;

    internal sealed record Rejected(string Message) : AutomationFormat1ConfigurationProjection;
}

internal static class AutomationFormat1ConfigurationProjector
{
    internal const string IdentityRedactedReason = "fixed-identity-redacted";
    internal const string IdentityPlaceholderReason = "fixed-identity-placeholder-preserved";

    internal static AutomationFormat1ConfigurationProjection Project(
        string definitionId,
        JsonElement configuration,
        IReadOnlyDictionary<string, AutomationInputBindingMode> bindingModes,
        int maximumCollectionRecords
    )
    {
        if (configuration.ValueKind != JsonValueKind.Object)
        {
            return Rejected("Automation configuration must be a JSON object.");
        }
        if (definitionId != AutomationDefinitionIds.CelTransform.Value)
        {
            return new AutomationFormat1ConfigurationProjection.Projected(
                configuration.Clone(),
                []
            );
        }

        if (
            !AutomationCelTransformDocumentSerializer.TryDeserialize<AutomationCelTransformDocument>(
                configuration,
                out var document
            )
        )
        {
            return Rejected("CEL Transform configuration does not match the Format 1 schema.");
        }

        if (document.Inputs.Count > maximumCollectionRecords)
        {
            return Rejected(
                $"The CEL Transform input collection exceeds the {maximumCollectionRecords} record limit."
            );
        }
        if (document.Outputs.Count > maximumCollectionRecords)
        {
            return Rejected(
                $"The CEL Transform output collection exceeds the {maximumCollectionRecords} record limit."
            );
        }

        var inputs = new AutomationCelTransformInputDocument[document.Inputs.Count];
        var identityRedacted = false;
        var identityPlaceholderPreserved = false;
        for (var index = 0; index < document.Inputs.Count; index++)
        {
            var input = document.Inputs[index];
            if (
                input.ValueType == AutomationPortValueType.Arguments.ToString()
                && input.FixedValue.ValueKind == JsonValueKind.Array
                && input.FixedValue.GetArrayLength() > maximumCollectionRecords
            )
            {
                return Rejected(
                    $"A fixed CEL Arguments value exceeds the {maximumCollectionRecords} record limit."
                );
            }

            if (!IsIdentity(input.ValueType))
            {
                inputs[index] = input with
                {
                    FixedValue = SanitizeFixedValue(
                        input.FixedValue,
                        ref identityRedacted,
                        ref identityPlaceholderPreserved
                    ),
                };
                continue;
            }

            var isFixed =
                !bindingModes.TryGetValue(input.BindingFieldId, out var mode)
                || mode == AutomationInputBindingMode.Fixed;
            if (
                isFixed
                && input.FixedValue.ValueKind == JsonValueKind.Null
                && input.Nullability == nameof(AutomationPortNullability.Nullable)
            )
            {
                inputs[index] = input;
                continue;
            }
            if (isFixed)
            {
                if (
                    AutomationTransferPlaceholder.Is(
                        input.FixedValue,
                        AutomationTransferPlaceholder.Identity
                    )
                )
                {
                    identityPlaceholderPreserved = true;
                    inputs[index] = input;
                }
                else
                {
                    identityRedacted = true;
                    inputs[index] = input with
                    {
                        FixedValue = AutomationTransferPlaceholder.Create(
                            AutomationTransferPlaceholder.Identity
                        ),
                    };
                }
                continue;
            }

            identityRedacted = true;
            inputs[index] = input with
            {
                FixedValue = AutomationCelTransformDocumentSerializer.Serialize(
                    new AutomationCelIdentityDocument(string.Empty, string.Empty)
                ),
            };
        }

        var reasons = ImmutableArray.CreateBuilder<string>(2);
        if (identityRedacted)
        {
            reasons.Add(IdentityRedactedReason);
        }
        if (identityPlaceholderPreserved)
        {
            reasons.Add(IdentityPlaceholderReason);
        }
        return new AutomationFormat1ConfigurationProjection.Projected(
            AutomationCelTransformDocumentSerializer.Serialize(document with { Inputs = inputs }),
            reasons.ToImmutable()
        );
    }

    private static AutomationFormat1ConfigurationProjection.Rejected Rejected(string message) =>
        new(message);

    private static bool IsIdentity(string valueType) =>
        valueType
            is nameof(AutomationPortValueType.Actor)
                or nameof(AutomationPortValueType.Channel);

    private static JsonElement SanitizeFixedValue(
        JsonElement value,
        ref bool identityRedacted,
        ref bool identityPlaceholderPreserved
    )
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteSanitizedFixedValue(
                writer,
                value,
                ref identityRedacted,
                ref identityPlaceholderPreserved
            );
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteSanitizedFixedValue(
        Utf8JsonWriter writer,
        JsonElement value,
        ref bool identityRedacted,
        ref bool identityPlaceholderPreserved
    )
    {
        if (AutomationTransferPlaceholder.Is(value, AutomationTransferPlaceholder.Identity))
        {
            value.WriteTo(writer);
            identityPlaceholderPreserved = true;
            return;
        }
        if (value.ValueKind == JsonValueKind.Object && HasIdentityMember(value))
        {
            AutomationTransferPlaceholder.Write(writer, AutomationTransferPlaceholder.Identity);
            identityRedacted = true;
            return;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                WriteSanitizedFixedValue(
                    writer,
                    property.Value,
                    ref identityRedacted,
                    ref identityPlaceholderPreserved
                );
            }
            writer.WriteEndObject();
            return;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
            {
                WriteSanitizedFixedValue(
                    writer,
                    item,
                    ref identityRedacted,
                    ref identityPlaceholderPreserved
                );
            }
            writer.WriteEndArray();
            return;
        }
        value.WriteTo(writer);
    }

    private static bool HasIdentityMember(JsonElement value)
    {
        foreach (var property in value.EnumerateObject())
        {
            if (
                string.Equals(
                    property.Name,
                    AutomationCelTransformDocumentFields.IdentityLogin,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    property.Name,
                    AutomationCelTransformDocumentFields.IdentityDisplayName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }
        }
        return false;
    }
}
