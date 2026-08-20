using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations.Page;

public sealed partial class AutomationEditorNode
{
    internal AutomationFlowDraftNode Draft() =>
        new(
            Id,
            new(Definition.Id.Value, Definition.Schema.Current.Value, ConfigurationJson()),
            AutomationExpressionLanguage.CurrentVersion,
            FailurePolicy,
            _bindings.ToImmutableDictionary(),
            Position,
            string.IsNullOrWhiteSpace(DisplayAlias) ? null : DisplayAlias
        );

    private JsonElement ConfigurationJson()
    {
        if (_transform is not null)
        {
            return TransformConfigurationJson();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var field in Definition.Configuration)
            {
                WriteField(writer, field, _values[field.Id]);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteField(
        Utf8JsonWriter writer,
        AutomationConfigurationFieldMetadata field,
        string value
    )
    {
        if (!field.Required && string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        switch (field.FieldType)
        {
            case AutomationConfigurationFieldType.Number:
            case AutomationConfigurationFieldType.Duration:
                if (long.TryParse(value, out var number))
                {
                    writer.WriteNumber(field.Id.Value, number);
                }
                else
                {
                    writer.WriteNull(field.Id.Value);
                }

                break;
            case AutomationConfigurationFieldType.Data data:
                WriteDataValue(
                    writer,
                    field.Id.Value,
                    data.ValueType,
                    field.Required
                        ? AutomationPortNullability.NonNullable
                        : AutomationPortNullability.Nullable,
                    value
                );
                break;
            case AutomationConfigurationFieldType.Reference
            {
                ReferenceKind: AutomationReferenceKind.CustomCommand,
            }:
                if (int.TryParse(value, out var commandId))
                {
                    writer.WriteNumber(field.Id.Value, commandId);
                }
                else
                {
                    writer.WriteNull(field.Id.Value);
                }

                break;
            default:
                writer.WriteString(field.Id.Value, value);
                break;
        }
    }

    private JsonElement TransformConfigurationJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("inputs");
            writer.WriteStartArray();
            foreach (var input in _transform!.Inputs)
            {
                writer.WriteStartObject();
                writer.WriteString("port-id", input.PortId.Value);
                writer.WriteString("cel-identifier", input.Identifier.Value);
                writer.WriteString("display-name", input.DisplayName);
                writer.WriteString("binding-field-id", input.BindingFieldId.Value);
                writer.WriteString("type", input.ValueType.ToString());
                writer.WriteString("nullability", input.Nullability.ToString());
                writer.WritePropertyName("fixed");
                WriteAutomationValue(
                    writer,
                    IsComplexFixedValue(input.ValueType)
                        ? input.FixedValue
                        : ParseFixedValue(
                            _values[input.BindingFieldId],
                            input.ValueType,
                            input.Nullability
                        )
                );
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("outputs");
            writer.WriteStartArray();
            foreach (var output in _transform.Outputs)
            {
                writer.WriteStartObject();
                writer.WriteString("port-id", output.PortId.Value);
                writer.WriteString("display-name", output.DisplayName);
                writer.WriteString("type", output.ValueType.ToString());
                writer.WriteString("nullability", output.Nullability.ToString());
                writer.WriteString("cel", output.Source);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}
