using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

internal static partial class AutomationCelTransform
{
    private static bool TryParseInput(
        AutomationCelTransformInputDocument document,
        out AutomationCelTransformInput input
    )
    {
        input = null!;
        if (
            !TryEnum(document.ValueType, out AutomationPortValueType valueType)
            || !TryEnum(document.Nullability, out AutomationPortNullability nullability)
            || !TryValue(document.FixedValue, valueType, nullability, out var fixedValue)
        )
        {
            return false;
        }

        input = new(
            new(document.PortId),
            new(document.Identifier),
            document.DisplayName,
            new(document.BindingFieldId),
            valueType,
            nullability,
            fixedValue
        );
        return true;
    }

    private static bool TryParseOutput(
        AutomationCelTransformOutputDocument document,
        out AutomationCelTransformOutput output
    )
    {
        output = null!;
        if (
            !TryEnum(document.ValueType, out AutomationPortValueType valueType)
            || !TryEnum(document.Nullability, out AutomationPortNullability nullability)
        )
        {
            return false;
        }

        output = new(
            new(document.PortId),
            document.DisplayName,
            valueType,
            nullability,
            document.Source
        );
        return true;
    }

    internal static bool TryValue(
        JsonElement json,
        AutomationPortValueType type,
        AutomationPortNullability nullability,
        out AutomationValue value
    )
    {
        value = null!;
        if (json.ValueKind == JsonValueKind.Null)
        {
            if (nullability != AutomationPortNullability.Nullable)
            {
                return false;
            }

            value = new AutomationValue.Null(type);
            return true;
        }

        AutomationValue? parsed = type switch
        {
            AutomationPortValueType.Text when json.ValueKind == JsonValueKind.String =>
                new AutomationValue.Text(json.GetString()!),
            AutomationPortValueType.Number when json.TryGetDecimal(out var number) =>
                new AutomationValue.Number(number),
            AutomationPortValueType.Boolean
                when json.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                new AutomationValue.Boolean(json.GetBoolean()),
            AutomationPortValueType.Timestamp
                when json.ValueKind == JsonValueKind.String
                    && json.TryGetDateTimeOffset(out var timestamp) =>
                new AutomationValue.Timestamp(timestamp),
            AutomationPortValueType.Actor => TryActor(json),
            AutomationPortValueType.Channel => TryChannel(json),
            AutomationPortValueType.Stream => TryStream(json),
            AutomationPortValueType.Arguments => TryArguments(json),
            _ => null,
        };
        if (parsed is null)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static AutomationValue? TryActor(JsonElement json) =>
        AutomationCelTransformDocumentSerializer.TryDeserialize<AutomationCelIdentityDocument>(
            json,
            out var identity
        )
            ? new AutomationValue.Actor(new(identity.Login, identity.DisplayName))
            : null;

    private static AutomationValue? TryChannel(JsonElement json) =>
        AutomationCelTransformDocumentSerializer.TryDeserialize<AutomationCelIdentityDocument>(
            json,
            out var identity
        )
            ? new AutomationValue.Channel(new(identity.Login, identity.DisplayName))
            : null;

    private static AutomationValue? TryStream(JsonElement json) =>
        AutomationCelTransformDocumentSerializer.TryDeserialize<AutomationCelStreamDocument>(
            json,
            out var stream
        )
            ? new AutomationValue.Stream(new(stream.Title, stream.GameName, stream.StartedAt))
            : null;

    private static AutomationValue? TryArguments(JsonElement json) =>
        json.ValueKind == JsonValueKind.Array
        && json.EnumerateArray().All(static value => value.ValueKind == JsonValueKind.String)
            ? new AutomationValue.Arguments([
                .. json.EnumerateArray()
                    .Select(
                        static (value, position) =>
                            new AutomationValueArgument(
                                position,
                                value.GetString()!,
                                [AutomationValueProvenance.Generated]
                            )
                    ),
            ])
            : null;

    private static bool TryEnum<T>(string text, out T value)
        where T : struct, Enum
    {
        value = default;
        return Enum.TryParse(text, out value) && Enum.IsDefined(value) && text == value.ToString();
    }

    private static bool HasDuplicates(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Count() != values.Count();

    private static bool Scalar(AutomationPortValueType type) =>
        type
            is AutomationPortValueType.Text
                or AutomationPortValueType.Number
                or AutomationPortValueType.Boolean
                or AutomationPortValueType.Timestamp;

    private static bool Matches(
        AutomationPortValueType type,
        AutomationPortNullability nullability,
        AutomationValue value
    ) =>
        value switch
        {
            AutomationValue.Null nullValue => nullability == AutomationPortNullability.Nullable
                && nullValue.ValueType == type,
            _ => AutomationPureHandlerRegistry.ValueType(value) == type,
        };

    private static AutomationConfigurationParseResult Invalid(string fieldId, string message) =>
        new AutomationConfigurationParseResult.Invalid([
            new(new AutomationValidationTarget.Field(new(fieldId)), message),
        ]);

    private static AutomationValidationResult InvalidResult(string fieldId, string message) =>
        AutomationValidationResult.Invalid(
            new AutomationValidationTarget.Field(new(fieldId)),
            message
        );
}
