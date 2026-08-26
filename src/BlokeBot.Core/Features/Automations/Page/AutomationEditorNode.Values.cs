using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations.Page;

public sealed partial class AutomationEditorNode
{
    private static void WriteAutomationValue(Utf8JsonWriter writer, AutomationValue value)
    {
        switch (value)
        {
            case AutomationValue.Nil:
                writer.WriteNullValue();
                break;
            case AutomationValue.Text text:
                writer.WriteStringValue(text.Value);
                break;
            case AutomationValue.Number number:
                writer.WriteNumberValue(number.Value);
                break;
            case AutomationValue.Boolean boolean:
                writer.WriteBooleanValue(boolean.Value);
                break;
            case AutomationValue.Timestamp timestamp:
                writer.WriteStringValue(timestamp.Value);
                break;
            case AutomationValue.Actor actor:
                writer.WriteStartObject();
                writer.WriteString("login", actor.Value.Login);
                writer.WriteString("display-name", actor.Value.DisplayName);
                writer.WriteEndObject();
                break;
            case AutomationValue.Channel channel:
                writer.WriteStartObject();
                writer.WriteString("login", channel.Value.Login);
                writer.WriteString("display-name", channel.Value.DisplayName);
                writer.WriteEndObject();
                break;
            case AutomationValue.Stream stream:
                writer.WriteStartObject();
                writer.WriteString("title", stream.Value.Title);
                writer.WriteString("game-name", stream.Value.GameName);
                if (stream.Value.StartedAtUtc is { } startedAt)
                {
                    writer.WriteString(
                        "started-at",
                        startedAt.ToString("O", CultureInfo.InvariantCulture)
                    );
                }
                else
                {
                    writer.WriteNull("started-at");
                }
                writer.WriteEndObject();
                break;
            case AutomationValue.Arguments arguments:
                writer.WriteStartArray();
                foreach (var argument in arguments.Values)
                {
                    writer.WriteStringValue(argument.Value);
                }
                writer.WriteEndArray();
                break;
            case AutomationValue.Array array:
                AutomationStructuredValue.Write(writer, array);
                break;
            case AutomationValue.Map map:
                AutomationStructuredValue.Write(writer, map);
                break;
            case AutomationValue.Null:
                writer.WriteNullValue();
                break;
        }
    }

    private static int IndexOf<T>(ImmutableArray<T> values, Func<T, bool> predicate)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (predicate(values[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string DefaultValue(AutomationConfigurationFieldMetadata field) =>
        field.FieldType switch
        {
            AutomationConfigurationFieldType.Number number => number.Minimum.ToString(
                CultureInfo.InvariantCulture
            ),
            AutomationConfigurationFieldType.Duration duration => field.Id.Value.EndsWith(
                "milliseconds",
                StringComparison.Ordinal
            )
                ? ((long)duration.Minimum.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)
                : ((long)duration.Minimum.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            AutomationConfigurationFieldType.Choice choice => choice.Values[0],
            AutomationConfigurationFieldType.Data { ValueType: AutomationPortValueType.Number } =>
                "0",
            AutomationConfigurationFieldType.Data { ValueType: AutomationPortValueType.Boolean } =>
                bool.FalseString,
            AutomationConfigurationFieldType.Data
            {
                ValueType: AutomationPortValueType.Timestamp,
            } => DateTimeOffset.UnixEpoch.ToString("O", CultureInfo.InvariantCulture),
            _ => string.Empty,
        };

    private static string ReadValue(JsonElement configuration, AutomationConfigurationFieldId id) =>
        configuration.TryGetProperty(id.Value, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                JsonValueKind.Array or JsonValueKind.Object => value.GetRawText(),
                _ => string.Empty,
            }
            : string.Empty;
}
