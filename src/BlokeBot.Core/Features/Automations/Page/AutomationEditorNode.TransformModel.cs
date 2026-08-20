using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations.Page;

public sealed partial class AutomationEditorNode
{
    private void RefreshTransformDefinition()
    {
        if (_transform is not null)
        {
            Definition = EffectiveTransformDefinition(Definition, _transform);
        }
    }

    private static AutomationCelTransformConfiguration? ParseTransform(
        AutomationFlowDraftNode node,
        AutomationDefinitionDescriptor definition
    )
    {
        if (definition.Id != AutomationDefinitionIds.CelTransform)
        {
            return null;
        }

        var parser = AutomationCelTransform.Definition(definition.Id, definition.Display);
        return
            parser.Parse(node.Definition.Configuration)
                is AutomationConfigurationParseResult.Parsed
                {
                    Configuration: AutomationCelTransformConfiguration configuration,
                }
            ? configuration
            : null;
    }

    private static AutomationDefinitionDescriptor EffectiveTransformDefinition(
        AutomationDefinitionDescriptor registered,
        AutomationCelTransformConfiguration configuration
    )
    {
        var definition = AutomationCelTransform.Definition(registered.Id, registered.Display);
        return ((IAutomationEffectiveDefinition)definition).EffectiveDescriptor(configuration);
    }

    private static AutomationCelTransformConfiguration DefaultTransform() =>
        new(
            [
                new(
                    new("input-value"),
                    new("value"),
                    "Value",
                    new("binding-value"),
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    new AutomationValue.Text(string.Empty)
                ),
            ],
            [
                new(
                    new("output-result"),
                    "Result",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    "value"
                ),
            ]
        );

    private static string NextTransformSequence(IEnumerable<string> values, string prefix)
    {
        var existing = values.ToHashSet(StringComparer.Ordinal);
        for (var sequence = 1; ; sequence++)
        {
            var candidate = $"{prefix}_{sequence}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static void WriteDataValue(
        Utf8JsonWriter writer,
        string name,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability,
        string value
    )
    {
        writer.WritePropertyName(name);
        WriteAutomationValue(writer, ParseFixedValue(value, valueType, nullability));
    }

    private static AutomationValue ParseFixedValue(
        string value,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability
    ) =>
        nullability == AutomationPortNullability.Nullable && string.IsNullOrWhiteSpace(value)
            ? new AutomationValue.Null(valueType)
            : valueType switch
            {
                AutomationPortValueType.Text => new AutomationValue.Text(value),
                AutomationPortValueType.Number
                    when decimal.TryParse(
                        value,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var number
                    ) => new AutomationValue.Number(number),
                AutomationPortValueType.Boolean when bool.TryParse(value, out var boolean) =>
                    new AutomationValue.Boolean(boolean),
                AutomationPortValueType.Timestamp
                    when DateTimeOffset.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var timestamp
                    ) => new AutomationValue.Timestamp(timestamp),
                AutomationPortValueType.Arguments => new AutomationValue.Arguments([]),
                AutomationPortValueType.Actor => new AutomationValue.Actor(
                    new(string.Empty, string.Empty)
                ),
                AutomationPortValueType.Channel => new AutomationValue.Channel(
                    new(string.Empty, string.Empty)
                ),
                AutomationPortValueType.Stream => new AutomationValue.Stream(new(null, null, null)),
                _ => new AutomationValue.Null(valueType),
            };

    internal static bool IsComplexFixedValue(AutomationPortValueType valueType) =>
        valueType
            is AutomationPortValueType.Arguments
                or AutomationPortValueType.Actor
                or AutomationPortValueType.Channel
                or AutomationPortValueType.Stream;

    private static bool TryParseComplexFixedValue(
        string source,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability,
        out AutomationValue value
    )
    {
        try
        {
            using var document = JsonDocument.Parse(source);
            return AutomationCelTransform.TryValue(
                document.RootElement,
                valueType,
                nullability,
                out value
            );
        }
        catch (JsonException)
        {
            value = null!;
            return false;
        }
    }

    private static string DisplayFixedValue(AutomationValue value) =>
        value switch
        {
            AutomationValue.Text text => text.Value,
            AutomationValue.Number number => number.Value.ToString(CultureInfo.InvariantCulture),
            AutomationValue.Boolean boolean => boolean.Value.ToString(),
            AutomationValue.Timestamp timestamp => timestamp.Value.ToString(
                "O",
                CultureInfo.InvariantCulture
            ),
            AutomationValue.Arguments
            or AutomationValue.Actor
            or AutomationValue.Channel
            or AutomationValue.Stream => SerializeAutomationValue(value),
            AutomationValue.Null => string.Empty,
            _ => string.Empty,
        };

    private static string SerializeAutomationValue(AutomationValue value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteAutomationValue(writer, value);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
